using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Ptarmigan.Models;
using NBitcoin;
using NBitcoin.RPC;

namespace BTCPayServer.Lightning.Ptarmigan
{
    public class PtarmiganLightningClient : ILightningClient
    {

        private readonly Network _network;
        private readonly RPCClient _rpcClient;
        private readonly PtarmiganClient _ptarmiganClient;
        private readonly Uri _address;
        private readonly string _apiToken;

        public PtarmiganLightningClient(Uri address, string apiToken, Network network, RPCClient rpcClient, HttpClient httpClient = null)
        {

            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _address = address;
            _network = network;
            _rpcClient = rpcClient;
            _apiToken = apiToken;
            _ptarmiganClient = new PtarmiganClient(address, apiToken, httpClient);
        }

        public async Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo)
        {
            await _ptarmiganClient.Connect(nodeInfo.NodeId, nodeInfo.Host, nodeInfo.Port);
            return ConnectionResult.Ok;
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default(CancellationToken))
        {
			var info = await _ptarmiganClient.GetInfo(cancellation);
			var nodeInfo = new LightningNodeInformation()
			{
				BlockHeight = info.Result.BlockCount ?? 0
			};

			if (!String.IsNullOrEmpty(info.Result.AnnounceIp))
			{
				nodeInfo.NodeInfoList.Add(new NodeInfo(new PubKey(info.Result.NodeId),
										  info.Result.AnnounceIp.Split(':')[0],
										  info.Result.NodePort));
			}
			return nodeInfo;
        }

        public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation = default(CancellationToken))
        {
            var result = await _ptarmiganClient.CreateInvoice(
                amount.MilliSatoshi,
                description,
                Convert.ToInt32(expiry.TotalSeconds),
                cancellation);

            var parsed = BOLT11PaymentRequest.Parse(result.Result.Bolt11, _network);
            var invoice = new LightningInvoice()
            {
                BOLT11 = result.Result.Bolt11,
                Amount = amount,
                Id = result.Result.Hash,
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = parsed.ExpiryDate
            };
            return invoice;
        }
        Task<LightningInvoice> ILightningClient.CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation)
        {
            if (req.DescriptionHash != null)
            {
                throw new NotSupportedException();
            }
            return (this as ILightningClient).CreateInvoice(req.Amount, req.Description, req.Expiry, cancellation);
        }

        public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default(CancellationToken))
        {
            var response = await _ptarmiganClient.SendPayment(bolt11, 0, cancellation);

            if (response.Error != null)
            {
                return new PayResponse(PayResult.CouldNotFindRoute);
            }

            while (true)
            {
                var payments = await _ptarmiganClient.ListPayment(response.Result.PaymentId);
                var payment = payments.Result[0];

                if (payment.State == "succeeded")
                {
                    return new PayResponse(PayResult.Ok);
                }
                if (payment.State == "failed")
                {
                    return new PayResponse(PayResult.CouldNotFindRoute);
                }
                if (payment.State == "processing")
                {
                    await Task.Delay(1000);
                }
            }
        }

        public async Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation)
        {

            var result = await _ptarmiganClient.OpenChannel(openChannelRequest.NodeInfo.NodeId,
                    openChannelRequest.ChannelAmount.Satoshi, 0,
                    Convert.ToInt64(openChannelRequest.FeeRate.FeePerK));

            if (result.Error != null)
            {
                if (result.Error.Message.Contains("not connected"))
                {
                    return new OpenChannelResponse(OpenChannelResult.PeerNotConnected);
                }

                if (result.Error.Message.Contains("channel already opened"))
                {
                    return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);
                }

                return new OpenChannelResponse(OpenChannelResult.CannotAffordFunding);
            }

            var info = await _ptarmiganClient.GetInfo(cancellation);
            var node = info.Result.Peers.Find(peer => peer.NodeId == openChannelRequest.NodeInfo.NodeId.ToString());

            if (node == null)
            {
                return new OpenChannelResponse(OpenChannelResult.CannotAffordFunding);
            }

            if (node.Status == "none")
            {
                return new OpenChannelResponse(OpenChannelResult.AlreadyExists);
            }

            if (node.Status == "establishing")
            {
                return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);
            }

            return new OpenChannelResponse(OpenChannelResult.Ok);

        }

        public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default(CancellationToken))
        {
            var result = await _ptarmiganClient.ListInvoice(invoiceId, cancellation);
            if (result.Result.Count == 0)
            {
                return null;
            }
            return GetLightningInvoiceObject(result.Result[0], _network);
        }

        public async Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default(CancellationToken))
        {
            var result = await _ptarmiganClient.GetInfo(cancellation);
            if (result.Result == null || result.Result.Peers.Count == 0)
            {
                return null;
            }
            var peers = result.Result.Peers;
            return peers.Select(peer =>
            {
                return new LightningChannel()
                {
                    IsPublic = true,
                    RemoteNode = new PubKey(peer.NodeId),
                    IsActive = peer.Status.Contains("normal operation") && peer.Role != "none",
                    LocalBalance = peer.Local != null ? new LightMoney(peer.Local.Msatoshi) : 0,
                    Capacity = peer.Local != null && peer.Remote != null ? new LightMoney(peer.Local.Msatoshi + peer.Remote.Msatoshi) : 0,
                    ChannelPoint = peer.FundingTx != null ? new OutPoint(new uint256(peer.FundingTx), peer.FundingVout) : null
                };
            }).ToArray();
        }

        public async Task<BitcoinAddress> GetDepositAddress()
        {
            if (_rpcClient == null)
            {
                throw new NotSupportedException("The bitcoind connection details were not provided.");
            }
            return await _rpcClient.GetNewAddressAsync();
        }

        public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default(CancellationToken))
        {
            return new PtarmiganSession(
                await WebsocketHelper.CreateClientWebSocket(_address.ToString(), "Bearer " + _apiToken, cancellation), _network, this);
        }

        internal LightningInvoice GetLightningInvoiceObject(ListInvoiceResultResponse invoice, Network network)
        {
            var parsed = BOLT11PaymentRequest.Parse(invoice.Bolt11, network);

            var lightningInvoice = new LightningInvoice()
            {
                Id = invoice.Hash,
                Amount = invoice.AmountMsat,
                AmountReceived = invoice.AmountMsat,
                BOLT11 = invoice.Bolt11,
                Status = ToStatus(invoice.State),
                PaidAt = null,
                ExpiresAt = parsed.ExpiryDate
            };

            if (invoice.State == "used")
            {
                lightningInvoice.PaidAt = parsed.ExpiryDate;
            }

            return lightningInvoice;

        }

        private static LightningInvoiceStatus ToStatus(string status)
        {
            switch (status)
            {
                case "used":
                    return LightningInvoiceStatus.Paid;
                case "unused":
                    return LightningInvoiceStatus.Unpaid;
                case "expire":
                    return LightningInvoiceStatus.Expired;
                case "unknown":
                    throw new NotSupportedException($"'{status}' can't map to any LightningInvoiceStatus");
                default:
                    throw new NotSupportedException($"'{status}' can't map to any LightningInvoiceStatus");
            }
        }
    }
}