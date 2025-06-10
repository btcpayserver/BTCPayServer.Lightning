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

            if (info != null && info.PaymentHash != "")
            {
                var parsed = BOLT11PaymentRequest.Parse(info.Invoice, _network);
                var invoiceId = info.PaymentHash;
                var lnInvoice = new LightningInvoice
                {
                    Id = invoiceId,
                    PaymentHash = info.PaymentHash,
                    Amount = parsed.MinimumAmount,
                    ExpiresAt = parsed.ExpiryDate,
                    BOLT11 = info.Invoice
                };
                if (DateTimeOffset.UtcNow >= parsed.ExpiryDate)
                {
                    lnInvoice.Status = LightningInvoiceStatus.Expired;
                }
                lnInvoice.AmountReceived = new LightMoney(info.ReceivedSat, LightMoneyUnit.Satoshi);
                lnInvoice.Status = info.IsPaid ? LightningInvoiceStatus.Paid : LightningInvoiceStatus.Unpaid;
                lnInvoice.PaidAt = info.CompletedAt;
                lnInvoice.Preimage = info.PreImage;
                return lnInvoice;
            }
            return null;
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

        public Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        public async Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default) =>
            await ListInvoices(null, cancellation);

        public async Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default)
        {
            GetOutgoingPaymentResponse info = await _PhoenixdClient.GetOutgoingPayment(paymentHash, null, cancellation);
            var payment = new LightningPayment
            {
                Preimage = info.preImage,
                PaymentHash = info.paymentHash,
                CreatedAt = info.createdAt,
                Amount = new LightMoney(info.sent, LightMoneyUnit.Satoshi),
                AmountSent = new LightMoney(info.sent + info.fees, LightMoneyUnit.Satoshi),
                Fee = new LightMoney(info.fees, LightMoneyUnit.Satoshi),
                Status = info.isPaid ? LightningPaymentStatus.Complete : LightningPaymentStatus.Unknown
            };

            return payment;
        }

        public Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        public Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        async Task<LightningInvoice> ILightningClient.CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation)
        {
            var result = await _PhoenixdClient.CreateInvoice(
                description,
                amount.MilliSatoshi / 1000,
                Convert.ToInt32(expiry.TotalSeconds), null, cancellation);

            var parsed = BOLT11PaymentRequest.Parse(result.Serialized, _network);
            var invoice = new LightningInvoice
            {
                BOLT11 = result.Serialized,
                Amount = amount,
                Id = result.PaymentHash,
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = parsed.ExpiryDate,
                PaymentHash = result.PaymentHash
            };
            return invoice;
        }

        Task<LightningInvoice> ILightningClient.CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation)
        {
            return (this as ILightningClient).CreateInvoice(req.Amount, req.DescriptionHash is not null ? req.DescriptionHash.ToString() : req.Description, req.Expiry, cancellation);
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

            if (NormalizeChain(network) != NormalizeChain(info.Chain))
                throw new PhoenixdApiException { Error = new PhoenixdApiError { Error = $"Chain mismatch: BTCPay Server is using \"{network}\" while Phoenixd is configured for \"{info.Chain}\""} };

            var nodeInfo = new LightningNodeInformation
            {
                BlockHeight = info.BlockHeight,
                Version = info.Version
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
