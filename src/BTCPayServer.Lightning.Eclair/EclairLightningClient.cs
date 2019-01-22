using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.RPC;
using WebSocketSharp;

namespace BTCPayServer.Lightning.Eclair
{
    public class EclairLightningClient : ILightningClient
    {
        private readonly Uri _address;
        private readonly Network _network;
        private readonly RPCClient _rpcClient;

        private readonly ConcurrentDictionary<string, LightningInvoice> _memoryInvoices =
            new ConcurrentDictionary<string, LightningInvoice>();

        private EclairClient _eclairClient;

        public EclairLightningClient(Uri address, string password, Network network, RPCClient rpcClient)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _address = address;
            _network = network;
            _rpcClient = rpcClient;
            _eclairClient = new EclairClient(address, password);
        }


        public async Task<LightningInvoice> GetInvoice(string invoiceId,
            CancellationToken cancellation = default(CancellationToken))
        {
            return await UpdateLocalLightningInvoice(invoiceId, cancellation);
        }

        public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation = default(CancellationToken))
        {
            var result = await _eclairClient.Receive(
                description,
                amount.MilliSatoshi,
                Convert.ToInt32(expiry.TotalSeconds), cancellation);

            var decodedBolt = BOLT11PaymentRequest.Parse(result, _network);

            var invoice = new LightningInvoice()
            {
                BOLT11 = result,
                Amount = decodedBolt.MinimumAmount,
                Id = decodedBolt.PaymentHash.ToString(),
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = decodedBolt.ExpiryDate
            };

            _memoryInvoices.TryAdd(invoice.Id, invoice);
            return invoice;
        }

        public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default(CancellationToken))
        {
            return new EclairWebsocketListener(this);
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default(CancellationToken))
        {
            var info = await _eclairClient.GetInfo(cancellation);

            //HACK: public ip cannot use host name in eclair...
            var host = info.Alias;
            return new LightningNodeInformation()
            {
                NodeInfo = new NodeInfo(new PubKey(info.NodeId), host,
                    info.Port == 0 ? 9735 : info.Port),
                BlockHeight = info.BlockHeight
            };
        }

        public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default(CancellationToken))
        {
            try
            {
                var sendResult = await _eclairClient.Send(bolt11, cancellation);
                if (sendResult.failures != null && sendResult.failures.Any(item =>
                        item.t.Equals("route not found", StringComparison.InvariantCultureIgnoreCase)))
                {
                    return new PayResponse(PayResult.CouldNotFindRoute);
                }

                return new PayResponse(PayResult.Ok);
            }
            catch (Exception)
            {
                return new PayResponse(PayResult.CouldNotFindRoute);
            }
        }

        public async Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest,
            CancellationToken cancellation = default(CancellationToken))
        {
            try
            {
                var result = await _eclairClient.OpenChannel(openChannelRequest.NodeInfo.NodeId.ToString(),
                    openChannelRequest.ChannelAmount.Satoshi
                    , 0,
                    Convert.ToInt64(openChannelRequest.FeeRate.SatoshiPerByte), cancellation);

                if (result.Contains("already exists"))
                {
                    return new OpenChannelResponse(OpenChannelResult.AlreadyExists); 
                }
                
                return new OpenChannelResponse(OpenChannelResult.Ok);
            }
            catch (Exception e)
            {
                if (e.Message.Contains("command failed: not connected") || e.Message.Contains("command failed: no connection to peer"))
                {
                    return new OpenChannelResponse(OpenChannelResult.PeerNotConnected);
                }

                if (e.Message.Contains("command failed: Insufficient funds"))
                {
                    return new OpenChannelResponse(OpenChannelResult.CannotAffordFunding);
                }
                return  new OpenChannelResponse(OpenChannelResult.AlreadyExists);
            }
        }

        public async Task<BitcoinAddress> GetDepositAddress()
        {
            return await _rpcClient.GetNewAddressAsync();
        }

        public async Task ConnectTo(NodeInfo nodeInfo)
        {
            await _eclairClient.ConnectToNode(nodeInfo.NodeId.ToString(), nodeInfo.Host, nodeInfo.Port);
        }

        private async Task<LightningInvoice> UpdateLocalLightningInvoice(string paymentHash,
            CancellationToken cancellation)
        {
            var paid = await _eclairClient.CheckPayment(paymentHash, cancellation);
            if (!_memoryInvoices.ContainsKey(paymentHash))
                return new LightningInvoice()
                {
                    Id = paymentHash,
                    Status = paid ? LightningInvoiceStatus.Paid : LightningInvoiceStatus.Unpaid,
                    PaidAt = paid ? DateTimeOffset.Now : (DateTimeOffset?) null
                };
            var invoice = _memoryInvoices[paymentHash];
            if (invoice.ExpiresAt <= DateTimeOffset.Now && !paid)
            {
                invoice.Status = LightningInvoiceStatus.Expired;
            }
            else if (paid)
            {
                invoice.Status = LightningInvoiceStatus.Paid;
                invoice.PaidAt = DateTimeOffset.Now;
            }
            else
            {
                invoice.Status = LightningInvoiceStatus.Unpaid;
            }

            return invoice;
        }

        public class EclairWebsocketListener : ILightningInvoiceListener
        {
            private readonly EclairLightningClient _eclairLightningClient;
            private readonly string _address;
            private WebSocket _websocketConnection;
            private ConcurrentQueue<string> _receivedInvoiceQueue = new ConcurrentQueue<string>();

            public EclairWebsocketListener(EclairLightningClient eclairLightningClient)
            {
                _eclairLightningClient = eclairLightningClient;
                _address = new Uri(_eclairLightningClient._address, "ws")
                    .AbsoluteUri
                    .Replace("https", "wss")
                    .Replace("http", "ws");

                Connect();
            }

            private void Connect()
            {
                _websocketConnection = new WebSocket(_address);
                _websocketConnection.OnMessage += WebsocketConnectionOnOnMessage;
                _websocketConnection.OnError += (sender, args) => { Console.WriteLine(args.Message); };
                _websocketConnection.Connect();
            }

            private void WebsocketConnectionOnOnMessage(object sender, MessageEventArgs e)
            {
                if (e.IsText)
                {
                    _receivedInvoiceQueue.Enqueue(e.Data);
                }
            }

            public void Dispose()
            {
                _websocketConnection.Close();
            }

            public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
            {
                while (!cancellation.IsCancellationRequested)
                {
                    if (!_websocketConnection.IsAlive)
                    {
                        Dispose();
                        Connect();
                    }

                    if (_receivedInvoiceQueue.IsEmpty)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellation);
                        continue;
                    }

                    if (!_receivedInvoiceQueue.TryDequeue(out var paymentHash)) continue;

                    return await _eclairLightningClient.UpdateLocalLightningInvoice(paymentHash, cancellation);
                }

                return null;
            }
        }
    }
}