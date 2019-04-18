using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Eclair.Models;
using NBitcoin;
using NBitcoin.RPC;

namespace BTCPayServer.Lightning.Eclair
{
    public class EclairLightningClient : ILightningClient
    {
        private readonly Uri _address;
        private readonly string _password;
        private readonly Network _network;
        private readonly RPCClient _rpcClient;
        private readonly  EclairClient _eclairClient;

        public EclairLightningClient(Uri address, string password, Network network, RPCClient rpcClient)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _address = address;
            _password = password;
            _network = network;
            _rpcClient = rpcClient;
            _eclairClient = new EclairClient(address, password);
        }


        public async Task<LightningInvoice> GetInvoice(string invoiceId,
            CancellationToken cancellation = default(CancellationToken))
        {
            var result = await _eclairClient.GetInvoice(invoiceId, cancellation);
            GetReceivedInfoResponse info;
            try
            {
                info = await _eclairClient.GetReceivedInfo(invoiceId, null, cancellation);
            }
            catch (EclairClient.EclairApiException e)
            {
                info = new GetReceivedInfoResponse()
                {
                    AmountMsat = 0,
                    ReceivedAt = 0,
                    PaymentHash = invoiceId
                };
            }
            
            var parsed = BOLT11PaymentRequest.Parse(result.Serialized, _network);
            return new LightningInvoice()
            {
                Id = result.PaymentHash,
                Amount = parsed.MinimumAmount,
                ExpiresAt = parsed.ExpiryDate,
                BOLT11 = result.Serialized,
                AmountReceived = info.AmountMsat,
                Status = info.AmountMsat >= parsed.MinimumAmount? LightningInvoiceStatus.Paid: DateTime.Now >= parsed.ExpiryDate? LightningInvoiceStatus.Expired: LightningInvoiceStatus.Unpaid,
                PaidAt = info.ReceivedAt == 0? (DateTimeOffset?) null:  DateTimeOffset.FromUnixTimeMilliseconds(info.ReceivedAt)
            };
        }

        public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation = default(CancellationToken))
        {
            var result = await _eclairClient.CreateInvoice(
                description,
                amount.MilliSatoshi,
                Convert.ToInt32(expiry.TotalSeconds), null, cancellation);

            var parsed = BOLT11PaymentRequest.Parse(result.Serialized, _network);
            var invoice = new LightningInvoice()
            {
                BOLT11 = result.Serialized,
                Amount = amount,
                Id = result.PaymentHash,
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = parsed.ExpiryDate
            };
            return invoice;
        }

        public Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default(CancellationToken))
        {
            return Task.FromResult<ILightningInvoiceListener>(new EclairWebsocketListener(this, _password));
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default(CancellationToken))
        {
            var info = await _eclairClient.GetInfo(cancellation);

            //HACK: public ip cannot use host name in eclair...
            var host = info.Alias;
            return new LightningNodeInformation()
            {
                NodeInfoList = new List<NodeInfo>()
                {
                    new NodeInfo(new PubKey(info.NodeId), host,9735 )  
                },
                BlockHeight = info.BlockHeight
            };
        }

        public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default(CancellationToken))
        {
            try
            {
                await _eclairClient.PayInvoice(bolt11, null,null, cancellation);
                return new PayResponse(PayResult.Ok);
            }
            catch (EclairClient.EclairApiException)
            {
                return new PayResponse(PayResult.CouldNotFindRoute);
            }
        }

        public async Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest,
            CancellationToken cancellation = default(CancellationToken))
        {
            try
            {
                var result = await _eclairClient.Open(openChannelRequest.NodeInfo.NodeId,
                    openChannelRequest.ChannelAmount.Satoshi
                    , null,
                    Convert.ToInt64(openChannelRequest.FeeRate.SatoshiPerByte), null,  cancellation);

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
            await _eclairClient.Connect(nodeInfo.NodeId, nodeInfo.Host, nodeInfo.Port);
        }

        public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public class EclairWebsocketListener : ILightningInvoiceListener
        {
            private readonly EclairLightningClient _eclairLightningClient;
            
            private readonly string _address;
            private ConcurrentQueue<string> _receivedInvoiceQueue = new ConcurrentQueue<string>();
            private EclairWebsocketClient _eclairWebsocketClient;

            public EclairWebsocketListener(EclairLightningClient eclairLightningClient, string password)
            {
                _eclairLightningClient = eclairLightningClient;
                _address = WebsocketHelper.ToWebsocketUri(new Uri(_eclairLightningClient._address, "ws").AbsoluteUri);
                _eclairWebsocketClient = new EclairWebsocketClient(_address, password);
                _eclairWebsocketClient.PaymentReceivedEvent +=EclairWebsocketClientOnPaymentReceivedEvent;
                
                _eclairWebsocketClient.Connect();
            }

            private void EclairWebsocketClientOnPaymentReceivedEvent(object sender, PaymentReceivedEvent e)
            {
                _receivedInvoiceQueue.Enqueue(e.PaymentHash);
            }

            public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
            {
                while (!cancellation.IsCancellationRequested)
                {
                    if (_receivedInvoiceQueue.IsEmpty)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellation);
                        continue;
                    }

                    if (!_receivedInvoiceQueue.TryDequeue(out var paymentHash)) continue;

                    return await _eclairLightningClient.GetInvoice(paymentHash, cancellation);
                }

                return null;
            }

            public void Dispose()
            {
                _eclairWebsocketClient.Dispose();
                
            }
        }
    }
}