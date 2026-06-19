using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using BTCPayServer.Lightning.Phoenixd.Models;
using NBitcoin;

namespace BTCPayServer.Lightning.Phoenixd
{
    public class PhoenixdLightningClient : ILightningClient
    {
        private readonly Uri _address;
        private readonly string _username;
        private readonly string _password;
        private readonly Network _network;
        private readonly PhoenixdClient _PhoenixdClient;
        public static PhoenixdClient PhoenixdClientInstance { get; private set; }

        private string NormalizeChain(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            // Converts chain name to a single letter to distinguish main chains (for example Main/mainnet from testnet, but not testnet3 from testnet4)
            return input.Substring(0, 1).ToLowerInvariant();
        }

        private async Task<LightningInvoice> ToLightningInvoice(string PaymentHash, CancellationToken cancellation)
        {
            GetIncomingPaymentResponse info = null;
            try
            {
                info = await _PhoenixdClient.GetIncomingPayment(PaymentHash, null, cancellation);
            }
            catch (PhoenixdClient.PhoenixdApiException)
            {
            }

            return ToLightningInvoice(info);
        }

        private LightningInvoice ToLightningInvoice(GetIncomingPaymentResponse info)
        {
            if (info == null || string.IsNullOrEmpty(info.PaymentHash) || string.IsNullOrEmpty(info.Invoice))
                return null;

            var parsed = BOLT11PaymentRequest.Parse(info.Invoice, _network);
            return new LightningInvoice
            {
                Id = info.PaymentHash,
                PaymentHash = info.PaymentHash,
                Amount = parsed.MinimumAmount,
                ExpiresAt = info.ExpiresAt ?? parsed.ExpiryDate,
                BOLT11 = info.Invoice,
                // Phoenixd may charge recipient liquidity fees; include them so the invoice is not considered underpaid.
                AmountReceived = LightMoney.Satoshis(info.ReceivedSat) + new LightMoney(info.Fees, LightMoneyUnit.MilliSatoshi),
                Status = info.IsPaid ? LightningInvoiceStatus.Paid :
                    (info.IsExpired || DateTimeOffset.UtcNow >= parsed.ExpiryDate ? LightningInvoiceStatus.Expired : LightningInvoiceStatus.Unpaid),
                PaidAt = info.CompletedAt,
                Preimage = info.PreImage
            };
        }

        private LightningPayment ToLightningPayment(GetOutgoingPaymentResponse info)
        {
            if (info == null || string.IsNullOrEmpty(info.paymentHash))
                return null;

            var fee = new LightMoney(info.fees, LightMoneyUnit.MilliSatoshi);
            var sent = LightMoney.Satoshis(info.sent);
            return new LightningPayment
            {
                Id = info.paymentId,
                Preimage = info.preImage,
                PaymentHash = info.paymentHash,
                CreatedAt = info.createdAt,
                Amount = sent,
                AmountSent = sent + fee,
                Fee = fee,
                BOLT11 = info.invoice,
                Status = info.isPaid ? LightningPaymentStatus.Complete : LightningPaymentStatus.Unknown
            };
        }

        public PhoenixdLightningClient(Uri address, string password, Network network, HttpClient httpClient = null) :
            this(address, null, password, network, httpClient)
        {
        }

        public PhoenixdLightningClient(Uri address, string username, string password, Network network, HttpClient httpClient = null)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _address = address;
            _username = username;
            _password = password;
            _network = network;
            _PhoenixdClient = new PhoenixdClient(address, username, password, network, httpClient);
            PhoenixdClientInstance = _PhoenixdClient;
        }

        public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            try
            {
                return await ToLightningInvoice(invoiceId, cancellation);
            }
            catch (PhoenixdClient.PhoenixdApiException)
            {
                return null;
            }
        }

        public async Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation = default) =>
            await GetInvoice(paymentHash.ToString(), cancellation);

        public async Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = default)
        {
            request ??= new ListInvoicesParams();
            var incoming = await _PhoenixdClient.ListIncomingPayments(true, request.OffsetIndex, 100, cancellation);
            var invoices = incoming.Select(ToLightningInvoice).Where(i => i != null);
            if (request.PendingOnly is true)
                invoices = invoices.Where(i => i.Status == LightningInvoiceStatus.Unpaid);
            return invoices.ToArray();
        }

        public async Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default) =>
            await ListInvoices(null, cancellation);

        public async Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default)
        {
            try
            {
                return ToLightningPayment(await _PhoenixdClient.GetOutgoingPayment(paymentHash, null, cancellation));
            }
            catch (PhoenixdClient.PhoenixdApiException)
            {
                return null;
            }
        }

        public async Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default) =>
            await ListPayments(null, cancellation);

        public async Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = default)
        {
            request ??= new ListPaymentsParams();
            var outgoing = await _PhoenixdClient.ListOutgoingPayments(request.IncludePending is true, request.OffsetIndex, 100, cancellation);
            return outgoing.Select(ToLightningPayment).Where(p => p != null).ToArray();
        }

        Task<LightningInvoice> ILightningClient.CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation)
            => (this as ILightningClient).CreateInvoice(new CreateInvoiceParams(amount, description, expiry),
                cancellation);

        async Task<LightningInvoice> ILightningClient.CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation)
        {
            var result = await _PhoenixdClient.CreateInvoice(req.DescriptionHash is null ? req.Description : null,
                req.Amount.MilliSatoshi / 1000, Convert.ToInt32(req.Expiry.TotalSeconds), null,
                req.DescriptionHash?.ToString(), cancellation);
            var parsed = BOLT11PaymentRequest.Parse(result.Serialized, _network);
            return new LightningInvoice
            {
                BOLT11 = result.Serialized,
                Amount = LightMoney.Satoshis(result.AmountSat),
                Id = result.PaymentHash,
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = parsed.ExpiryDate,
                PaymentHash = result.PaymentHash
            };
        }

        public static async Task<ClientWebSocket> ClientWebSocket(string url, string authorizationValue, CancellationToken cancellation = default)
        {
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", authorizationValue);
            var uri = new UriBuilder(url) { UserName = null, Password = null }.Uri.AbsoluteUri;
            if (!uri.EndsWith("/"))
                uri += "/";
            uri += "websocket";
            uri = WebsocketHelper.ToWebsocketUri(uri);

            await socket.ConnectAsync(new Uri(uri), cancellation);
            return socket;
        }

        public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default)
        {
            return new PhoenixdSession(
               await ClientWebSocket(_address.AbsoluteUri,
                  new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.Default.GetBytes($"{_username??string.Empty}:{_password}"))).ToString(), cancellation), this);
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default)
        {
            var network = _network.ToString();
            var info = await _PhoenixdClient.GetInfo(cancellation);

            if (!string.IsNullOrEmpty(info.Chain) && NormalizeChain(network) != NormalizeChain(info.Chain))
                throw new PhoenixdApiException { Error = new PhoenixdApiError { Error = $"Chain mismatch: BTCPay Server is using \"{network}\" while Phoenixd is configured for \"{info.Chain}\""} };

            var nodeInfo = new LightningNodeInformation
            {
                BlockHeight = info.BlockHeight,
                Version = info.Version,
                Alias = "Phoenixd",
                Color = "3399ff",
                ActiveChannelsCount = info.Channels?.Count(c => c.State == "Normal"),
                InactiveChannelsCount = info.Channels?.Count(c => c.State != "Normal"),
                PendingChannelsCount = info.Channels?.Count(c => c.State != "Normal")
            };
            return nodeInfo;
        }

        public async Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = default)
        {
            var balance = await _PhoenixdClient.GetBalance(cancellation);

            var onchain = new OnchainBalance
            {
                // Not supported by Phoenixd
                Confirmed = null,
                Unconfirmed = null,
                Reserved = null
            };
            var offchain = new OffchainBalance
            {
                Opening = 0,
                Local = new LightMoney(balance.balanceSat + balance.feeCreditSat, LightMoneyUnit.Satoshi),
                Remote = 0,
                Closing = 0
            };
            return new LightningNodeBalance(onchain, offchain);
        }

        public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default)
        {
            return await Pay(bolt11, null, cancellation);
        }

        public async Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            // Cancel after timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var timeout = payParams?.SendTimeout ?? PayInvoiceParams.DefaultSendTimeout;
            cts.CancelAfter(timeout);

            var info = await _PhoenixdClient.PayInvoice(bolt11, payParams?.Amount?.MilliSatoshi / 1000, cts.Token);
            if (info.PaymentPreimage is not null)
            {
                return new PayResponse(PayResult.Ok,
                    new PayDetails
                    {
                        TotalAmount = new LightMoney(info.RecipientAmountSat, LightMoneyUnit.Satoshi),
                        FeeAmount = new LightMoney(info.RoutingFeeSat, LightMoneyUnit.Satoshi),
                        PaymentHash = new uint256(info.PaymentHash),
                        Preimage = new uint256(info.PaymentPreimage),
                        Status = LightningPaymentStatus.Complete
                    });
            }
            else if (info.Reason is not null)
            {
                switch (info.Reason)
                {
                    case "this invoice has already been paid":
                        return new PayResponse(PayResult.Ok);
                    case "channel is not connected yet, please retry when connected":
                    case "channel creation is in progress, please retry when ready":
                    case "channel closing is in progress, please retry when a new channel has been created":
                    case "payment could not be sent through existing channels, check individual failures for more details":
                    case "not enough funds in wallet to afford payment":
                    case "the recipient was offline or did not have enough liquidity to receive the payment":
                        return new PayResponse(PayResult.CouldNotFindRoute, info.Reason);
                    default:
                        return new PayResponse(PayResult.Unknown, info.Reason);
                }
            }
            return new PayResponse(PayResult.Unknown);
        }

        public Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest,
            CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        public Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default)
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

        public override string ToString()
        {
            var result= $"type=phoenixd;server={_address}";
            if (_username is { })
                result += $";username={_username}";
            if (_password is { })
                result += $";password={_password}";
            return result;
        }

        internal class PhoenixdApiException : Exception
        {
            public PhoenixdApiError Error { get; set; }

            public override string Message => Error?.Error;
        }

        internal class PhoenixdApiError
        {
            public string Error { get; set; }
        }
    }
}
