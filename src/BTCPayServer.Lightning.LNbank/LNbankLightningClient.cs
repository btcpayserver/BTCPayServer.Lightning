using System;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using Microsoft.AspNetCore.SignalR.Client;

namespace BTCPayServer.Lightning.LNbank
{
    public class LNbankLightningClient : ILightningClient
    {
        private readonly LNbankClient _client;
        private readonly Uri _baseUri;
        private readonly string _apiToken;

        public LNbankLightningClient(Uri baseUri, string apiToken, Network network, HttpClient httpClient = null)
        {
            _baseUri = baseUri;
            _apiToken = apiToken;
            _client = new LNbankClient(baseUri, apiToken, network, httpClient);
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default)
        {
            var data = await _client.GetInfo(cancellation);

            var nodeInfo = new LightningNodeInformation
            {
                BlockHeight = data.BlockHeight
            };
            foreach (var nodeUri in data.NodeURIs)
            {
                if (NodeInfo.TryParse(nodeUri, out var info))
                    nodeInfo.NodeInfoList.Add(info);
            }

            return nodeInfo;
        }

        public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            var invoice = await _client.GetInvoice(invoiceId, cancellation);

            return new LightningInvoice
            {
                Id = invoice.Id,
                Amount = invoice.Amount,
                PaidAt = invoice.PaidAt,
                ExpiresAt = invoice.ExpiresAt,
                BOLT11 = invoice.BOLT11,
                Status = invoice.Status,
                AmountReceived = invoice.AmountReceived
            };
        }

        public async Task<BitcoinAddress> GetDepositAddress()
        {
            return await _client.GetDepositAddress();
        }

        public async Task CancelInvoice(string invoiceId)
        {
            await _client.CancelInvoice(invoiceId, CancellationToken.None);
        }

        public async Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default)
        {
            var channels = await _client.ListChannels(cancellation);

            return channels.Select(channel => new LightningChannel
            {
                IsPublic = channel.IsPublic,
                IsActive = channel.IsActive,
                RemoteNode = new PubKey(channel.RemoteNode),
                LocalBalance = channel.LocalBalance,
                Capacity = channel.Capacity,
                ChannelPoint = OutPoint.Parse(channel.ChannelPoint),
            }).ToArray();
        }

        public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation = default)
        {
            var invoice = await _client.CreateInvoice(amount, description, expiry, cancellation);

            return new LightningInvoice
            {
                Id = invoice.Id,
                Amount = invoice.Amount,
                PaidAt = invoice.PaidAt,
                ExpiresAt = invoice.ExpiresAt,
                BOLT11 = invoice.BOLT11,
                Status = invoice.Status,
                AmountReceived = invoice.AmountReceived
            };
        }

        public async Task<LightningInvoice> CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation = default)
        {
            return await (this as ILightningClient).CreateInvoice(req.Amount, req.Description, req.Expiry, cancellation);
        }

        public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default)
        {
            return await _client.Pay(bolt11, cancellation);
        }

        public async Task<OpenChannelResponse> OpenChannel(OpenChannelRequest req, CancellationToken cancellation = default)
        {
            OpenChannelResult result;
            try
            {
                await _client.OpenChannel(req.NodeInfo, req.ChannelAmount, req.FeeRate, cancellation);
                result = OpenChannelResult.Ok;
            }
            catch (LNbankClient.LNbankApiException ex)
            {
                switch (ex.ErrorCode)
                {
                    case "channel-already-exists":
                        result = OpenChannelResult.AlreadyExists;
                        break;
                    case "cannot-afford-funding":
                        result = OpenChannelResult.CannotAffordFunding;
                        break;
                    case "need-more-confirmations":
                        result = OpenChannelResult.NeedMoreConf;
                        break;
                    case "peer-not-connected":
                        result = OpenChannelResult.PeerNotConnected;
                        break;
                    default:
                        throw new NotSupportedException("Unknown OpenChannelResult");
                }
            }

            return new OpenChannelResponse(result);
        }

        public async Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo)
        {
            try
            {
                await _client.ConnectTo(nodeInfo);
                return ConnectionResult.Ok;
            }
            catch (LNbankClient.LNbankApiException ex)
            {
                switch (ex.ErrorCode)
                {
                    case "could-not-connect":
                        return ConnectionResult.CouldNotConnect;
                    default:
                        throw new NotSupportedException("Unknown ConnectionResult");
                }
            }
        }

        public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default)
        {
            var listener = new LNbankHubClient(_baseUri, _apiToken, this, cancellation);

            await listener.Start(cancellation);

            return listener;
        }
    }
}
