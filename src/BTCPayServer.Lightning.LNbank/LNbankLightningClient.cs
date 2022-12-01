using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LNbank.Models;
using NBitcoin;

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
                BlockHeight = data.BlockHeight,
                Alias = data.Alias,
                Color = data.Color,
                Version = data.Version,
                PeersCount = data.PeersCount,
                ActiveChannelsCount = data.ActiveChannelsCount,
                InactiveChannelsCount = data.InactiveChannelsCount,
                PendingChannelsCount = data.PendingChannelsCount
            };
            foreach (var nodeUri in data.NodeURIs)
            {
                if (NodeInfo.TryParse(nodeUri, out var info))
                    nodeInfo.NodeInfoList.Add(info);
            }

            return nodeInfo;
        }

        public async Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = default)
        {
            try
            {
                return await _client.GetBalance(cancellation);
            }
            catch (LNbankClient.LNbankApiException)
            {
                return null;
            }
        }

        public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            try
            {
                var invoice = await _client.GetInvoice(invoiceId, cancellation);
                return ToLightningInvoice(invoice);
            }
            catch (LNbankClient.LNbankApiException)
            {
                return null;
            }
        }

        public Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default)
        {
            return ListInvoices(null, cancellation);
        }

        public async Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = default)
        {
            var invoices = await _client.ListInvoices(request, cancellation);
            return invoices.Select(ToLightningInvoice).ToArray();
        }

        public async Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default)
        {
            try
            {
                var payment = await _client.GetPayment(paymentHash, cancellation);
                return ToLightningPayment(payment);
            }
            catch (LNbankClient.LNbankApiException)
            {
                return null;
            }
        }

        public Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default)
        {
            return ListPayments(null, cancellation);
        }

        public async Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = default)
        {
            var payments = await _client.ListPayments(request, cancellation);
            return payments.Select(ToLightningPayment).ToArray();
        }

        public async Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default)
        {
            return await _client.GetDepositAddress(cancellation);
        }

        public async Task CancelInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            await _client.CancelInvoice(invoiceId, cancellation);
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
            return await (this as ILightningClient).CreateInvoice(new CreateInvoiceParams(amount, description, expiry), cancellation);
        }

        public async Task<LightningInvoice> CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation = default)
        {
            var invoice = await _client.CreateInvoice(req, cancellation);
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

        public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default)
        {
            return await Pay(bolt11, null, cancellation);
        }

        public async Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            try
            {
                return await _client.Pay(bolt11, payParams, cancellation);
            }
            catch (LNbankClient.LNbankApiException exception)
            {
                switch (exception.ErrorCode)
                {
                    case "could-not-find-route":
                        return new PayResponse(PayResult.CouldNotFindRoute, exception.Message);
                    default:                        
                        return new PayResponse(PayResult.Error, exception.Message);
                }
            }
        }

        public Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
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

        public async Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = default)
        {
            try
            {
                await _client.ConnectTo(nodeInfo, cancellation);
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

        private static LightningInvoice ToLightningInvoice(InvoiceData invoice) => new()
        {
            Id = invoice.Id,
            Amount = invoice.Amount,
            PaidAt = invoice.PaidAt,
            ExpiresAt = invoice.ExpiresAt,
            BOLT11 = invoice.BOLT11,
            Status = invoice.Status,
            AmountReceived = invoice.AmountReceived
        };
        
        private static LightningPayment ToLightningPayment(PaymentData payment) => new()
        {
            Id = payment.Id,
            Amount = payment.TotalAmount != null && payment.FeeAmount != null ? payment.TotalAmount - payment.FeeAmount : null,
            AmountSent = payment.TotalAmount,
            CreatedAt = payment.CreatedAt,
            BOLT11 = payment.BOLT11,
            Preimage = payment.Preimage,
            PaymentHash = payment.PaymentHash,
            Status = payment.Status
        };
    }
}
