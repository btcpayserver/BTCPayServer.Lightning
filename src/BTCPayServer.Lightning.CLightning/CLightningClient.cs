using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.CLightning
{
    public enum CLightningErrorCode : int
    {
        /* Errors from `pay`, `sendpay`, or `waitsendpay` commands */
        IN_PROGRESS = 200,
        RHASH_ALREADY_USED = 201,
        UNPARSEABLE_ONION = 202,
        DESTINATION_PERM_FAIL = 203,
        TRY_OTHER_ROUTE = 204,
        ROUTE_NOT_FOUND = 205,
        ROUTE_TOO_EXPENSIVE = 206,
        INVOICE_EXPIRED = 207,
        NO_SUCH_PAYMENT = 208,
        UNSPECIFIED_ERROR = 209,
        STOPPED_RETRYING = 210,

        /* `fundchannel` or `withdraw` errors */
        MAX_EXCEEDED = 300,
        CANNOT_AFFORD = 301,
        OUTPUT_IS_DUST = 302,
        BROADCAST_FAIL = 303,
        STILL_SYNCING_BITCOIN = 304,

        /* `connect` errors */
        CONNECT_NO_KNOWN_ADDRESS = 400,
        CONNECT_ALL_ADDRESSES_FAILED = 401,

        /* Errors from `invoice` command */
        LABEL_ALREADY_EXISTS = 900,
        PREIMAGE_ALREADY_EXISTS = 901,
        INVOICE_WAIT_TIMED_OUT = 904,

        /*delinvoice errors */
        DATABASE_ERROR = -1,
        LABEL_DOES_NOT_EXIST = 905,
        STATUS_NOT_MATCHED = 906,

        /* rune errors */
        RUNE_NOT_AUTHORIZED = 1501,
        RUNE_NOT_PERMITTED = 1502,
        RUNE_BLACKLISTED = 1503,

        /* general errors */
        WRONG_PARAMETERS = -32602,
        GENERAL_ERROR = -1

    }
    public class LightningRPCException : Exception
    {
        public LightningRPCException(string message, int code) : this(message, (CLightningErrorCode)code)
        {
        }
        public LightningRPCException(string message, CLightningErrorCode code) : base(message)
        {
            Code = code;
        }

        public CLightningErrorCode Code
        {
            get;
        }

        [Obsolete("Use Code instead")]
        public int ErrorCode => (int)Code;
    }
    public class CLightningClient : ILightningClient
    {
        public Network Network
        {
            get; private set;
        }
        public Uri Address
        {
            get; private set;
        }

        readonly ClnRestTransport _rest;
        internal int? WaitAnyInvoiceTimeoutSeconds => _rest?.WaitAnyInvoiceTimeoutSeconds;

        public CLightningClient(Uri address, Network network)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (address.Scheme == "file")
            {
                address = new UriBuilder(address) { Scheme = "unix" }.Uri;
            }
            Address = address;
            Network = network;
        }

        public CLightningClient(CLightningRestSettings settings, Network network, HttpClient httpClient = null)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _rest = new ClnRestTransport(settings, httpClient);
            Address = settings.Server;
            Network = network;
        }

        public Task<GetInfoResponse> GetInfoAsync(CancellationToken cancellation = default)
        {
            return SendCommandAsync<GetInfoResponse>("getinfo", cancellation: cancellation);
        }

        public Task<ListFundsResponse> ListFundsAsync(CancellationToken cancellation = default)
        {
            return SendCommandAsync<ListFundsResponse>("listfunds", cancellation: cancellation);
        }

        public Task FundChannelAsync(OpenChannelRequest openChannelRequest, CancellationToken cancellation)
        {
            OpenChannelRequest.AssertIsSane(openChannelRequest);
            var parameters = new
            {
                id = openChannelRequest.NodeInfo.NodeId.ToString(),
                amount = openChannelRequest.ChannelAmount.Satoshi,
                feerate = openChannelRequest.FeeRate is null ? null : $"{openChannelRequest.FeeRate.FeePerK.Satoshi}perkb"
            };
            return SendCommandAsync<object>("fundchannel", parameters, true, cancellation: cancellation);
        }

        public Task ConnectAsync(NodeInfo nodeInfo, CancellationToken cancellation = default)
        {
            return SendCommandAsync<object>("connect", new { id = $"{nodeInfo.NodeId}@{nodeInfo.Host}:{nodeInfo.Port}" }, true, cancellation: cancellation);
        }

        static Encoding UTF8 = new UTF8Encoding(false);
        // Null values are left out, so optional parameters keep their lightningd defaults.
        static readonly JsonSerializer ParametersSerializer = JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

        // onSending runs once the request is ready and about to go out, after anything that can fail locally.
        internal async Task<T> SendCommandAsync<T>(string command, object parameters = null, bool noReturn = false, bool isArray = false, CancellationToken cancellation = default, Action onSending = null)
        {
            var namedParameters = parameters is null ? new JObject() : JObject.FromObject(parameters, ParametersSerializer);
            var result = _rest is null
                ? await CallSocketAsync(command, namedParameters, onSending, cancellation)
                : await _rest.CallAsync(command, namedParameters, onSending, cancellation);
            if (noReturn)
                return default;
            if (isArray)
            {
                return result.Children().First().Children().First().ToObject<T>();
            }
            return result.ToObject<T>();
        }

        // A lightningd error has an integer code and a string message. Anything else, like a code
        // sent as a string or a fraction, doesn't come from lightningd and leaves the outcome unknown.
        internal static bool TryParseRpcError(JToken error, out LightningRPCException exception)
        {
            exception = null;
            if (error is JObject obj
                && obj["message"] is JValue { Type: JTokenType.String } message
                && obj["code"] is JValue { Type: JTokenType.Integer, Value: long code }
                && code >= int.MinValue && code <= int.MaxValue)
                exception = new LightningRPCException((string)message, (int)code);
            return exception != null;
        }

        private async Task<JToken> CallSocketAsync(string command, JObject namedParameters, Action onSending, CancellationToken cancellation)
        {
            onSending?.Invoke();
            using (Socket socket = await Connect())
            {
                using (var networkStream = new NetworkStream(socket))
                {
                    using (var textWriter = new StreamWriter(networkStream, UTF8, 1024 * 10, true))
                    {
                        using (var jsonWriter = new JsonTextWriter(textWriter))
                        {
                            var req = new JObject();
                            req.Add("id", 0);
                            req.Add("jsonrpc", "2.0");
                            req.Add("method", command);
                            req.Add("params", namedParameters);
                            await req.WriteToAsync(jsonWriter, cancellation);
                            await jsonWriter.FlushAsync(cancellation);
                        }
                        await textWriter.FlushAsync();
                    }
                    await networkStream.FlushAsync(cancellation);
                    using (var textReader = new StreamReader(networkStream, UTF8, false, 1024 * 10, true))
                    {
                        using (var jsonReader = new JsonTextReader(textReader))
                        {
                            var resultAsync = JObject.LoadAsync(jsonReader, cancellation);

                            // without this hack resultAsync is blocking even if cancellation happen
                            using (cancellation.Register(() =>
                             {
                                 socket.Dispose();
                             }))
                            {
                                try
                                {
                                    var result = await resultAsync;
                                    var error = result.Property("error");
                                    if (error != null)
                                    {
                                        if (TryParseRpcError(error.Value, out var rpcError))
                                            throw rpcError;
                                        throw new IOException($"lightningd answered with an error without an integer code: {error.Value.ToString(Formatting.None)}");
                                    }
                                    return result["result"];
                                }
                                catch when (cancellation.IsCancellationRequested)
                                {
                                    cancellation.ThrowIfCancellationRequested();
                                    throw new NotSupportedException(); // impossible
                                }
                            }
                        }
                    }
                }
            }
        }

        private async Task<Socket> Connect()
        {
            Socket socket = null;
            EndPoint endpoint = null;
            if (Address.Scheme == "tcp" || Address.Scheme == "tcp")
            {
                var domain = Address.DnsSafeHost;
                if (!IPAddress.TryParse(domain, out IPAddress address))
                {
                    address = (await Dns.GetHostAddressesAsync(domain)).FirstOrDefault();
                    if (address == null)
                        throw new Exception("Host not found");
                }
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                endpoint = new IPEndPoint(address, Address.Port);
            }
            else if (Address.Scheme == "unix")
            {
                var path = Address.AbsoluteUri.Remove(0, "unix:".Length);
                if (!path.StartsWith("/"))
                    path = "/" + path;
                while (path.Length >= 2 && (path[0] != '/' || path[1] == '/'))
                {
                    path = path.Remove(0, 1);
                }
                if (path.Length < 2)
                    throw new FormatException("Invalid unix url");
                socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                endpoint = new UnixEndPoint(path);
            }
            else
                throw new NotSupportedException($"Protocol {Address.Scheme} for clightning not supported");

            await socket.ConnectAsync(endpoint);
            return socket;
        }

        public async Task<BitcoinAddress> NewAddressAsync(CancellationToken cancellation = default)
        {
            var obj = await SendCommandAsync<JObject>("newaddr", cancellation: cancellation);
            var addr = obj.Properties().First().Value.Value<string>();
            return BitcoinAddress.Create(addr, Network);
        }

        public async Task<CLightningChannel[]> ListChannelsAsync(ShortChannelId ShortChannelId = null, CancellationToken cancellation = default)
        {
            return await SendCommandAsync<CLightningChannel[]>("listchannels", new { short_channel_id = ShortChannelId?.ToString() }, false, true, cancellation);
        }
        public async Task<PeerChannel[]> ListPeerChannelsAsync(CancellationToken cancellation = default)
        {
            return await SendCommandAsync<PeerChannel[]>("listpeerchannels", null, false, true, cancellation);
        }

        async Task<LightningPayment> ILightningClient.GetPayment(string paymentHash, CancellationToken cancellation)
        {
            return await GetPayment(paymentHash, cancellation);
        }

        async Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation)
        {
            var payments = await SendCommandAsync<CLightningPayment[]>("listpays", new { payment_hash = paymentHash }, false, true, cancellation);
            return payments.Length == 0 ? null : ToLightningPayment(payments.Last());
        }

        async Task<LightningInvoice> ILightningClient.GetInvoice(string invoiceId, CancellationToken cancellation)
        {
            var invoices = await SendCommandAsync<CLightningInvoice[]>("listinvoices", new { label = invoiceId }, false, true, cancellation);
            if (invoices.Length == 0 && invoiceId.Length == 64)
            {
                var paymentHash = new uint256(invoiceId);
                return await GetInvoice(paymentHash, cancellation);
            }
            return invoices.Length == 0 ? null : ToLightningInvoice(invoices[0]);
        }

        public async Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation)
        {
            var invoices = await SendCommandAsync<CLightningInvoice[]>("listinvoices", new { payment_hash = paymentHash.ToString() }, false, true, cancellation);
            return invoices.Length == 0 ? null : ToLightningInvoice(invoices[0]);
        }

        async Task<LightningInvoice[]> ILightningClient.ListInvoices(CancellationToken cancellation)
        {
            return await ListInvoices(null, cancellation);
        }

        public async Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation)
        {
            var invoices = await SendCommandAsync<CLightningInvoice[]>("listinvoices", null, false, true, cancellation);
            if (request != null)
            {
                // we need to filter client-side, because the listinvoices command does not support these filters
                invoices = invoices.Where(invoice => 
                    (!request.PendingOnly.HasValue || request.PendingOnly.Value is false || ToInvoiceStatus(invoice.Status) == LightningInvoiceStatus.Unpaid) &&
                    (!request.OffsetIndex.HasValue || invoice.PayIndex >= request.OffsetIndex.Value)).ToArray();
            }

            return invoices.Select(ToLightningInvoice).ToArray();
        }

        async Task<LightningPayment[]> ILightningClient.ListPayments(CancellationToken cancellation)
        {
            return await ListPayments(null, cancellation);
        }

        public async Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation)
        {
            var payments = await SendCommandAsync<CLightningPayment[]>("listpays", null, false, true, cancellation);
            if (request != null)
            {
                // we need to filter client-side, because the listpays command does not support these filters
                payments = payments.Where(payment => 
                    ((request.IncludePending.HasValue && request.IncludePending.Value) || ToPaymentStatus(payment.Status) != LightningPaymentStatus.Pending) &&
                    (!request.OffsetIndex.HasValue || !payment.CreatedAt.HasValue || payment.CreatedAt.Value.ToUnixTimeMilliseconds() >= request.OffsetIndex.Value)).ToArray();
            }

            return payments.Select(ToLightningPayment).ToArray();
        }

        // Default of c-lightning's 'exemptfee' on the legacy 'pay' command: fees below this are
        // accepted regardless of the percentage ceiling. xpay has no equivalent knob, so it is
        // folded into the absolute 'maxfee' we compute from MaxFeePercent.
        const long CLightningExemptFeeMilliSatoshi = 5000;

        private async Task<PayResponse> PayAsync(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            var isKeysend = bolt11 == null;
            if (isKeysend)
            {
                if (payParams?.Destination is null)
                    throw new ArgumentNullException(nameof(payParams.Destination));
                if (payParams?.Amount is null)
                    throw new ArgumentNullException(nameof(payParams.Amount));
            }
            bolt11 = bolt11?.Replace("lightning:", "").Replace("LIGHTNING:", "");
                
            // Pay the invoice - cancel after timeout, potentially caused by hold invoices
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var timeout = payParams?.SendTimeout ?? PayInvoiceParams.DefaultSendTimeout;
            cts.CancelAfter(timeout);
            
            // Once the request may have gone out, nothing but the caller's cancellation escapes.
            var requestSent = false;
            try
            {
                var pr = bolt11 is null ? null : BOLT11PaymentRequest.Parse(bolt11, Network);

                // Normally, it should be possible to pay above the minimum amount, but CLN doesn't support it, unless the bolt amount is 0.
                var explicitAmount = pr?.MinimumAmount is null || pr?.MinimumAmount == LightMoney.Zero ?  payParams?.Amount : null;

                long? maxFeeFlat = payParams?.MaxFeeFlat is null ? null : new LightMoney(payParams?.MaxFeeFlat).MilliSatoshi;

                if (maxFeeFlat is null)
                {
                    if (payParams?.MaxFeePercent is { } feePercent)
                    {
                        // Before the switch to xpay, MaxFeePercent was handed to 'pay' as its native
                        // 'maxfeepercent' for every invoice, with 'exemptfee' left at its 5000msat default,
                        // so a payment was accepted when the fee was below *either* the percentage or
                        // 5000msat. xpay takes only an absolute 'maxfee' (which overrides both of those),
                        // so rebuild the same ceiling here as max(percentage, exemptfee).
                        // The percentage applies to the amount actually being paid: the explicit amount when
                        // we supply one (keysend, or a bolt11 without an amount), otherwise the amount carried
                        // by the invoice.
                        var feeBase = explicitAmount ?? pr?.MinimumAmount;
                        // 'maxfee' is expressed in millisatoshi (like the flat MaxFeeFlat branch above), so the
                        // percentage must be applied in millisatoshi as well.
                        if (feeBase is not null && feeBase != LightMoney.Zero)
                            maxFeeFlat = Math.Max(
                                (long)(feeBase.ToDecimal(LightMoneyUnit.MilliSatoshi) * (decimal)feePercent / 100m),
                                CLightningExemptFeeMilliSatoshi);
                    }
                }

                var command = isKeysend ? "xkeysend" : "xpay";
                object opts = isKeysend
                    ? new { destination = payParams.Destination.ToHex(), amount_msat = explicitAmount!.MilliSatoshi, maxfee = maxFeeFlat }
                    : new { invstring = bolt11, amount_msat = explicitAmount?.MilliSatoshi, maxfee = maxFeeFlat };
                var response = await SendCommandAsync<CLightningPayResponse>(command, opts, false, cancellation: cts.Token, onSending: () => requestSent = true);

                return new PayResponse(PayResult.Ok, new PayDetails
                {
                    TotalAmount = response.AmountSent,
                    FeeAmount = response.AmountSent - response.Amount,
                    PaymentHash = response.GetPaymentHash(),
                    Preimage = response.PaymentPreImage,
                    Status = LightningPaymentStatus.Complete
                });
            }
            catch (LightningRPCException ex) when (IsPaymentFailure(ex.Code))
            {
                var routingError = ex.Code == CLightningErrorCode.ROUTE_NOT_FOUND ||
                                   (ex.Code == CLightningErrorCode.STOPPED_RETRYING && !ex.Message.Contains("invalid payload"));
                var result =
                    routingError
                        ? PayResult.CouldNotFindRoute
                        : PayResult.Error;
                return new PayResponse(result, ex.Message);
            }
            catch (LightningRPCException ex)
            {
                // Any other error, -1 and the in-progress codes included, doesn't prove that nothing was sent.
                return new PayResponse(PayResult.Unknown, ex.Message);
            }
            catch (Exception ex) when (cts.Token.IsCancellationRequested && !cancellation.IsCancellationRequested)
            {
                if (bolt11 != null)
                    return await GetCompletePayment(bolt11, ex.Message, cancellation);
            }
            catch (Exception ex) when (requestSent && !cancellation.IsCancellationRequested)
            {
                // A broken connection, an HttpClient timeout or an answer that doesn't parse.
                // The request may have reached lightningd, so the outcome is unknown.
                return new PayResponse(PayResult.Unknown, ex.Message);
            }
            return new PayResponse(PayResult.Unknown);
        }

        private async Task<PayResponse> GetCompletePayment(string bolt11, string errorDetail, CancellationToken cancellation)
        {
            try
            {
                var pr = BOLT11PaymentRequest.Parse(bolt11, Network);
                var payments = await SendCommandAsync<CLightningPayment[]>("listpays", new { payment_hash = pr.PaymentHash?.ToString() }, false, true, cancellation);
                // A failed entry can belong to an earlier attempt while the one we sent is still
                // in flight, so only a complete payment settles the outcome.
                var complete = payments.LastOrDefault(payment => payment.Status == "complete");
                if (complete is null)
                    return new PayResponse(PayResult.Unknown, errorDetail);
                var response = ToLightningPayment(complete);
                return new PayResponse(PayResult.Ok, new PayDetails
                {
                    TotalAmount = response.AmountSent,
                    FeeAmount = response.Fee,
                    PaymentHash = new uint256(response.PaymentHash),
                    Preimage = new uint256(response.Preimage),
                    Status = response.Status
                });
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested)
            {
                // Any failure of the lookup, an RPC error or an answer that doesn't parse, leaves the outcome unknown.
                return new PayResponse(PayResult.Unknown, ex.Message);
            }
        }

        // The errors that prove nothing was sent. In plugins/xpay/xpay.c at v26.06.10, xpay returns
        // them only before its first attempt, which new_attempt() counts with
        // "attempt->partid = ++payment->total_num_attempts;" (line 1551).
        // - -32602 from the parameter and invoice checks in json_xpay_params(), xpay_core() and
        //   json_xkeysend(), and from common/json_param.c before the command runs.
        // - 207 from xpay_core(), "if (now > invexpiry) return command_fail(cmd, PAY_INVOICE_EXPIRED," (line 2879).
        // - 205 from check_onion_budget() and populate_private_layer(), which run before getroutes.
        // - 205, 206 and 210 that getroutes_done_err() passes on only under
        //   "if (payment->total_num_attempts == 0) { payment_give_up(aux_cmd, payment, code, ..." (line 1812).
        // xpay.c has no PAY_ROUTE_TOO_EXPENSIVE or PAY_STOPPED_RETRYING of its own. Both come only from
        // askrene's getroutes ("*ecode = PAY_STOPPED_RETRYING;" at plugins/askrene/child/mcf.c:1504, and
        // "*ecode = PAY_ROUTE_TOO_EXPENSIVE;" at mcf.c:1656, 1680 and 1784). xpay passes a getroutes code
        // on only under the check above. After an attempt it reports PAY_UNSPECIFIED_ERROR instead (line 1826),
        // and its other passed on errors (forward_error) all come from fetchinvoice, fetchbip353 and the
        // preapprove calls, which run before the payment starts.
        // xpay gives up without waiting for parts in flight unless xpay-slow-mode is set
        // ("if (!xpay->slow_mode) return true;" in should_finish_command(), line 454). So 203 from
        // update_knowledge_from_error() and 209, which can follow an attempt, leave the outcome unknown.
        static bool IsPaymentFailure(CLightningErrorCode code) => code is
            CLightningErrorCode.WRONG_PARAMETERS or
            CLightningErrorCode.ROUTE_NOT_FOUND or
            CLightningErrorCode.ROUTE_TOO_EXPENSIVE or
            CLightningErrorCode.INVOICE_EXPIRED or
            CLightningErrorCode.STOPPED_RETRYING;

        async Task<PayResponse> ILightningClient.Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation)
        {
            return await PayAsync(bolt11, payParams, cancellation);
        }

        async Task<PayResponse> ILightningClient.Pay(string bolt11, CancellationToken cancellation)
        {
            return await PayAsync(bolt11, null, cancellation);
        }

        public async Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            return await PayAsync(null, payParams, cancellation);
        }

        static NBitcoin.DataEncoders.DataEncoder InvoiceIdEncoder = NBitcoin.DataEncoders.Encoders.Base58;

        async Task<LightningInvoice> ILightningClient.CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation)
        {
            var req = new CreateInvoiceParams(amount, description, expiry);
            return await (this as ILightningClient).CreateInvoice(req, cancellation);
        }
        
        async Task<LightningInvoice> ILightningClient.CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation)
        {
            var amount = req.Amount;
            var msat = amount == LightMoney.Zero ? "any" : amount.MilliSatoshi.ToString();
            var expiry = Math.Max(0, (int)req.Expiry.TotalSeconds);
            var id = InvoiceIdEncoder.EncodeData(RandomUtils.GetBytes(20));
            
            var args = new
            {
                amount_msat = msat,
                label = id,
                description = req.Description ?? "",
                expiry,
                exposeprivatechannels = req.PrivateRouteHints,
                deschashonly = req.DescriptionHashOnly ? true : (bool?)null
            };

            CLightningInvoice invoice = await SendCommandAsync<CLightningInvoice>(
                "invoice",
                args,
                cancellation: cancellation);

            if (invoice is null)
                throw new InvalidOperationException("Bug in BTCPayServer.Lightning library, contact developers, code 52917");

            invoice.Label = id;
            invoice.MilliSatoshi = amount;
            invoice.Status = "unpaid";
            
            return ToLightningInvoice(invoice);
        }

        async Task<ConnectionResult> ILightningClient.ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation)
        {
            try
            {
                await ConnectAsync(nodeInfo, cancellation);
            }
            catch (LightningRPCException ex) when (ex.Code == CLightningErrorCode.CONNECT_ALL_ADDRESSES_FAILED || ex.Code == CLightningErrorCode.CONNECT_NO_KNOWN_ADDRESS)
            {
                return ConnectionResult.CouldNotConnect;
            }
            return ConnectionResult.Ok;
        }

        public async Task CancelInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            await SendCommandAsync<CLightningInvoice>("delinvoice", new { label = invoiceId, status = "unpaid" }, cancellation: cancellation);
        }

        async Task<LightningChannel[]> ILightningClient.ListChannels(CancellationToken cancellation)
        {
            var listChannels = await this.ListPeerChannelsAsync(cancellation);
            List<LightningChannel> channels = new List<LightningChannel>();
            foreach (var channel in listChannels)
            {
                channels.Add(new LightningChannel
                {
                    RemoteNode = new PubKey(channel.PeerId),
                    IsPublic = !channel.Private,
                    LocalBalance = channel.ToUs,
                    // short_channel_id is not available until the funding transaction is confirmed.
                    ChannelPoint = channel.FundingTxId is null || channel.FundingOutnum is null
                        ? null
                        : new OutPoint(channel.FundingTxId, channel.FundingOutnum.Value),
                    Capacity = channel.Total,
                    IsActive = channel.State == "CHANNELD_NORMAL"
                });
            }
            return channels.ToArray();
        }

        internal static LightningInvoice ToLightningInvoice(CLightningInvoice invoice) =>
            new LightningInvoice
            {
                Id = invoice.Label,
                PaymentHash = invoice.PaymentHash.ToString(),
                Preimage = invoice.PaymentPreimage?.ToString(),
                Amount = invoice.MilliSatoshi,
                AmountReceived = invoice.MilliSatoshiReceived,
                BOLT11 = invoice.BOLT11,
                Status = ToInvoiceStatus(invoice.Status),
                PaidAt = invoice.PaidAt,
                ExpiresAt = invoice.ExpiryAt
            };

        internal static LightningPayment ToLightningPayment(CLightningPayment payment) =>
            new LightningPayment
            {
                Id = payment.Label,
                Amount = payment.MilliSatoshi,
                AmountSent = payment.MilliSatoshiSent,
                Fee = payment.MilliSatoshiSent != null && payment.MilliSatoshi != null ? payment.MilliSatoshiSent - payment.MilliSatoshi : null,
                BOLT11 = payment.BOLT11,
                Status = ToPaymentStatus(payment.Status),
                CreatedAt = payment.CreatedAt,
                PaymentHash = payment.PaymentHash.ToString(),
                Preimage = payment.Preimage?.ToString()
            };

        public static LightningInvoiceStatus ToInvoiceStatus(string status)
        {
            switch (status)
            {
                case "paid":
                    return LightningInvoiceStatus.Paid;
                case "unpaid":
                    return LightningInvoiceStatus.Unpaid;
                case "expired":
                    return LightningInvoiceStatus.Expired;
                default:
                    throw new NotSupportedException($"'{status}' can't map to any LightningInvoiceStatus");
            }
        }

        public static LightningPaymentStatus ToPaymentStatus(string status)
        {
            switch (status)
            {
                case "pending":
                    return LightningPaymentStatus.Pending;
                case "failed":
                    return LightningPaymentStatus.Failed;
                case "complete":
                    return LightningPaymentStatus.Complete;
                default:
                    throw new NotSupportedException($"'{status}' can't map to any LightningPaymentStatus");
            }
        }

        async Task<ILightningInvoiceListener> ILightningClient.Listen(CancellationToken cancellation)
        {
            // Over clnrest each wait is short, and a payment can land between two waits. Starting
            // from the newest pay_index lets the next wait return it.
            var lastPayIndex = _rest is null
                ? CLightningInvoiceListener.NextPayment
                : await GetLastPayIndex(cancellation);
            return new CLightningInvoiceListener(this, lastPayIndex);
        }

        private async Task<long> GetLastPayIndex(CancellationToken cancellation)
        {
            var invoices = await SendCommandAsync<CLightningInvoice[]>("listinvoices", null, false, true, cancellation);
            return invoices.Select(invoice => (long?)invoice.PayIndex).Max() ?? 0;
        }

        async Task<LightningNodeInformation> ILightningClient.GetInfo(CancellationToken cancellation)
        {
            var info = await GetInfoAsync(cancellation);
            return ToLightningNodeInformation(info);
        }
        
        async Task<LightningNodeBalance> ILightningClient.GetBalance(CancellationToken cancellation)
        {
            var response = await ListFundsAsync(cancellation);
            return ToLightningNodeBalance(response);
        }

        async Task<OpenChannelResponse> ILightningClient.OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation)
        {
retry:
            try
            {
                await FundChannelAsync(openChannelRequest, cancellation);
            }
            catch (LightningRPCException ex) when (ex.Code == CLightningErrorCode.STILL_SYNCING_BITCOIN)
            {
                await Task.Delay(1000, cancellation);
                goto retry;
            }
            catch (LightningRPCException ex) when (ex.Code == CLightningErrorCode.CANNOT_AFFORD)
            {
                return new OpenChannelResponse(OpenChannelResult.CannotAffordFunding);
            }
            catch (LightningRPCException ex) when (ex.Code == CLightningErrorCode.CONNECT_ALL_ADDRESSES_FAILED ||
                                                   ex.Code == CLightningErrorCode.CONNECT_NO_KNOWN_ADDRESS ||
                                                   ex.Message == "Peer not connected" ||
                                                   ex.Message == "Unknown peer" ||
                                                   ex.Message == "Unable to connect, no address known for peer")
            {
               return new OpenChannelResponse(OpenChannelResult.PeerNotConnected);
            }
            catch (LightningRPCException ex) when (ex.Message.Contains("CHANNELD_AWAITING_LOCKIN"))
            {
                return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);
            }
            catch (LightningRPCException ex) when (
                ex.Message.Contains("CHANNELD_NORMAL") ||
                ex.Message.Contains("CHANNELD_SHUTTING_DOWN") ||
                ex.Message.Contains("CLOSINGD_SIGEXCHANGE") ||
                ex.Message.Contains("CLOSINGD_COMPLETE") ||
                ex.Message.Contains("AWAITING_UNILATERAL") ||
                ex.Message.Contains("FUNDING_SPEND_SEEN") ||
                ex.Message.Contains("ONCHAIN"))
            {
                return new OpenChannelResponse(OpenChannelResult.AlreadyExists);
            }
            return new OpenChannelResponse(OpenChannelResult.Ok);
        }

        async Task<BitcoinAddress> ILightningClient.GetDepositAddress(CancellationToken cancellation)
        {
            return await NewAddressAsync();
        }

        public static LightningNodeInformation ToLightningNodeInformation(GetInfoResponse info)
        {
            var pubkey = new PubKey(info.Id);
            var nodeInfo = new LightningNodeInformation
            {
                BlockHeight = info.BlockHeight,
                Alias = info.Alias,
                Color = info.Color,
                Version = info.Version,
                PeersCount = info.NumPeers,
                ActiveChannelsCount = info.NumActiveChannels,
                InactiveChannelsCount = info.NumInactiveChannels,
                PendingChannelsCount = info.NumPendingChannels
            };
            if (info.Address != null)
            {
                nodeInfo.NodeInfoList.AddRange(info.Address.Select(addr => new NodeInfo(pubkey, addr.Address, addr.Port == 0 ? 9735 : addr.Port)));
            }
            return nodeInfo;
        }

        private LightningNodeBalance ToLightningNodeBalance(ListFundsResponse response)
        {
            var confirmed = Money.Zero;
            var reserved = Money.Zero;
            var unconfirmed = Money.Zero;
            var opening = LightMoney.Zero;
            var local = LightMoney.Zero;
            var remote = LightMoney.Zero;
            var closing = LightMoney.Zero;

            foreach (var output in response.Outputs)
            {
                if (output.Reserved)
                {
                    reserved += output.Value;
                }
                else switch (output.Status)
                {
                    case "confirmed":
                        confirmed += output.Value;
                        break;
                    case "unconfirmed":
                        unconfirmed += output.Value;
                        break;
                }
            }
            
            foreach (var channel in response.Channels)
            {
                switch (channel.State)
                {
                    case "OPENINGD":
                    case "CHANNELD_AWAITING_LOCKIN":
                    case "DUALOPEND_OPEN_INIT":
                    case "DUALOPEND_AWAITING_LOCKIN":
                        opening += channel.LocalAmount;
                        break;
                    case "CHANNELD_SHUTTING_DOWN":
                    case "CLOSINGD_SIGEXCHANGE":
                    case "CLOSINGD_COMPLETE":
                    case "AWAITING_UNILATERAL":
                    case "FUNDING_SPEND_SEEN":
                    case "ONCHAIN":
                        closing += channel.LocalAmount;
                        break;
                    case "CHANNELD_NORMAL":
                        local += channel.LocalAmount;
                        remote += channel.TotalAmount - channel.LocalAmount;
                        break;
                }
            }

            var onchain = new OnchainBalance { Confirmed = confirmed, Reserved = reserved, Unconfirmed = unconfirmed };
            var offchain = new OffchainBalance { Opening = opening, Local = local, Remote = remote, Closing = closing };

            return new LightningNodeBalance(onchain, offchain);
        }

        public override string ToString()
        {
            if (_rest is null)
                return $"type=clightning;server={Address}";
            var settings = _rest.Settings;
            var builder = new StringBuilder($"type=clightning;server={settings.Server}");
            if (settings.Rune != null)
                builder.Append($";rune={settings.Rune}");
            if (settings.RuneFilePath != null)
                builder.Append($";runefilepath={settings.RuneFilePath}");
            return builder.ToString();
        }
    }

    class CLightningInvoiceListener : ILightningInvoiceListener
    {
        CancellationTokenSource _Cts = new CancellationTokenSource();
        CLightningClient _Parent;
        public CLightningInvoiceListener(CLightningClient parent, long lastPayIndex)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            _Parent = parent;
            lastInvoiceIndex = lastPayIndex;
        }
        public void Dispose()
        {
            _Cts.Cancel();
        }

        // Above any real pay_index, so the first wait returns the next payment.
        internal const long NextPayment = 99999999999;
        long lastInvoiceIndex;

        // A server that answers INVOICE_WAIT_TIMED_OUT much sooner than asked must not make the loop spin.
        static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
        // Over clnrest a broken request is retried, waiting twice as long each time, up to 30 seconds.
        static readonly TimeSpan MaxFailureDelay = TimeSpan.FromSeconds(30);

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
        {
            using (var cancellation2 = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _Cts.Token))
            {
                // Over clnrest each wait ends with INVOICE_WAIT_TIMED_OUT before HttpClient gives up.
                // The socket keeps a single open wait, as before.
                var timeout = _Parent.WaitAnyInvoiceTimeoutSeconds;
                var failureDelay = RetryDelay;
                while (true)
                {
                    var waiting = Stopwatch.StartNew();
                    try
                    {
                        var invoice = await _Parent.SendCommandAsync<CLightningInvoice>("waitanyinvoice", new { lastpay_index = lastInvoiceIndex, timeout }, cancellation: cancellation2.Token);
                        // Move the cursor only once the invoice converts, so a failure retries it instead of skipping it.
                        var paid = CLightningClient.ToLightningInvoice(invoice);
                        lastInvoiceIndex = invoice.PayIndex.Value;
                        return paid;
                    }
                    catch (LightningRPCException ex) when (ex.Code == CLightningErrorCode.INVOICE_WAIT_TIMED_OUT)
                    {
                        failureDelay = RetryDelay;
                        if (waiting.Elapsed < TimeSpan.FromSeconds(timeout.GetValueOrDefault() / 2.0))
                            await Task.Delay(RetryDelay, cancellation2.Token);
                    }
                    catch (Exception ex) when (timeout != null
                                               && ex is not LightningRPCException
                                               && !cancellation2.IsCancellationRequested)
                    {
                        // A proxy error page, clnrest unable to reach lightningd, an HttpClient timeout,
                        // an answer that doesn't parse or a rune file that can't be read. Only the caller's
                        // cancellation and a lightningd error other than 904 end the wait.
                        await Task.Delay(failureDelay, cancellation2.Token);
                        failureDelay = TimeSpan.FromTicks(Math.Min(failureDelay.Ticks * 2, MaxFailureDelay.Ticks));
                    }
                }
            }
        }
    }
}
