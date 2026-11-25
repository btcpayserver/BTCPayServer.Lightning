using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LNDhub.Models;
using NBitcoin;

namespace BTCPayServer.Lightning.LndHub
{
    public class LndHubLightningClient : ILightningClient
    {
        public  readonly LndHubClient Client;
        internal readonly Uri _baseUri;
        internal readonly string _login;
        internal readonly string _password;
        private readonly Network _network;

        public LndHubLightningClient(Uri baseUri, string login, string password, Network network, HttpClient httpClient = null )
        {
            _baseUri = baseUri;
            _login = login;
            _password = password;
            _network = network;
            Client = new LndHubClient(baseUri, login, password, network, httpClient);
        }

        public async Task<CreateAccountResponse> CreateAccount(CancellationToken cancellation = default)
        {
            return await Client.CreateAccount(cancellation);
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default)
        {
            var data = await Client.GetInfo(cancellation);
            if (data == null)
                throw new NotSupportedException("The LNDHub instance does not support GetInfo");

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
            if (data.Uris != null)
            {
                foreach (var nodeUri in data.Uris)
                {
                    if (NodeInfo.TryParse(nodeUri, out var info))
                        nodeInfo.NodeInfoList.Add(info);
                }
            }

            return nodeInfo;
        }

        public async Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = default)
        {
            var balance = await Client.GetBalance(cancellation);
            var offchain = new OffchainBalance
            {
                Local = balance.BTC.AvailableBalance
            };
            return new LightningNodeBalance(null, offchain);
        }

        public async Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default)
        {
            return await Client.GetDepositAddress(cancellation);
        }

        public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            var invoices = await Client.GetInvoices(cancellation);
            var data = invoices.FirstOrDefault(i => i.Id.ToString() == invoiceId);
            return data == null ? null : LndHubUtil.ToLightningInvoice(data);
        }

        public async Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation = default)
        {
            return await GetInvoice(paymentHash.ToString(), cancellation);
        }

        public async Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default)
        {
            return await ListInvoices(null, cancellation);
        }

        public async Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = default)
        {
            var invoices = await Client.GetInvoices(cancellation);
            if (request != null)
            {
                // we need to filter client-side, because LNDhub does not support these filters
                invoices = invoices.Where(invoice => 
                    (!request.PendingOnly.HasValue || request.PendingOnly.Value is false || LndHubUtil.ToLightningInvoiceStatus(invoice) == LightningInvoiceStatus.Unpaid) &&
                    (!request.OffsetIndex.HasValue || invoice.AddIndex >= request.OffsetIndex.Value)).ToArray();
            }
            return invoices.Select(LndHubUtil.ToLightningInvoice).ToArray();
        }

        public async Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default)
        {
            var payments = await Client.GetTransactions(cancellation);
            var data = payments.FirstOrDefault(i => i.PaymentHash?.ToString() == paymentHash);
            return data == null ? null : LndHubUtil.ToLightningPayment(data);
        }

        public async Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default)
        {
            return await ListPayments(null, cancellation);
        }

        public async Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = default)
        {
            var payments = await Client.GetTransactions(cancellation);
            if (request != null)
            {
                // we need to filter client-side, because LNDhub does not support these filters
                payments = payments.Where(payment =>
                    ((request.IncludePending.HasValue && request.IncludePending.Value) || LndHubUtil.ToLightningPaymentStatus(payment) != LightningPaymentStatus.Pending) &&
                    (!request.OffsetIndex.HasValue || !payment.Timestamp.HasValue || payment.Timestamp.Value.ToUnixTimeMilliseconds() >= request.OffsetIndex.Value)).ToArray();
            }
            return payments.Select(LndHubUtil.ToLightningPayment).ToArray();
        }

        public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation = default)
        {
            return await (this as ILightningClient).CreateInvoice(new CreateInvoiceParams(amount, description, expiry), cancellation);
        }

        public async Task<LightningInvoice> CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation = default)
        {
            var invoice = await Client.CreateInvoice(req, cancellation);

            // the response to addinvoice is incomplete, fetch the invoice to return that data
            return await GetInvoice(invoice.Id, cancellation);
        }

        public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default)
        {
            return await Pay(bolt11, null, cancellation);
        }

        public async Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            try
            {
                var pr = BOLT11PaymentRequest.Parse(bolt11, _network);
                var payAmount = payParams?.Amount ?? pr.MinimumAmount;
                var response = await Client.Pay(bolt11, payParams, cancellation);
                if (!string.IsNullOrEmpty(response.PaymentError))
                {
                    // Exact pre-send rejections from LNbits v1.7.1, wrapped by its LNDhub extension.
                    // These prove only that THIS call sent nothing, not that an earlier call did not pay.
                    // https://github.com/lnbits/lnbits/blob/v1.7.1/lnbits/core/services/payments.py
                    // Anything else may have occurred after submission and must remain Unknown.
                    var result = response.PaymentError switch
                    {
                        "Invalid invoice" or
                        "Payment failed. (Only incoming payments allowed.)" or
                        "Payment failed. (Bolt11 decoding failed.)" or
                        "Payment failed. (Amountless invoices not supported.)" or
                        "Payment failed. (Missig invoice amount.)" or
                        "Payment failed. (Missing invoice amount.)" or
                        "Payment failed. (Wallet does not have permission to pay invoices.)" or
                        "Payment failed. (It is not allowed to spend funds from this server.)" or
                        "Payment failed. (Server is shutting down.)" or
                        "Payment failed. (Internal payment not found.)" or
                        "Payment failed. (Invalid invoice. Bolt11 changed.)" or
                        "Payment failed. (Insufficient balance.)" or
                        "Payment failed. (Internal invoice already paid.)" or
                        "Payment failed. (Payment already paid.)" or
                        "Payment failed. (Failed payment was already paid on the fundingsource.)" or
                        "Payment failed. (Payment is failed node, retrying is not possible.)" => PayResult.Error,
                        _ => PayResult.Unknown
                    };
                    return new PayResponse(result, response.PaymentError);
                }
                var totalAmount = response.Decoded?.Amount;
                var feeAmount = response.PaymentRoute?.FeeMsat ??
                                (totalAmount is null ? null : totalAmount - payAmount);
                
                return new PayResponse(PayResult.Ok, new PayDetails
                {
                    TotalAmount = totalAmount,
                    FeeAmount = feeAmount,
                    Preimage = response.PaymentPreimage,
                    PaymentHash = response.PaymentHash ?? pr.PaymentHash,
                    Status = LightningPaymentStatus.Complete
                });
            }
            catch (LndHubClient.LndHubApiException exception)
            {
                // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#general-error-response
                var result = exception.ErrorCode == 5 ? PayResult.CouldNotFindRoute : PayResult.Error;
                return new PayResponse(result, exception.Message);
            }
        }

        public Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        async Task<ILightningInvoiceListener> ILightningClient.Listen(CancellationToken cancellation)
        {
            return await Client.CreateInvoiceSession(cancellation);
        }

        public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        public Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        public Task CancelInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

    }
}
