using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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

        /*delinvoice errors */
        DATABASE_ERROR = -1,
        LABEL_DOES_NOT_EXIST = 905,
        STATUS_NOT_MATCHED = 906,

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

        public Task<GetInfoResponse> GetInfoAsync(CancellationToken cancellation = default)
        {
            return SendCommandAsync<GetInfoResponse>("getinfo", cancellation: cancellation);
        }

        public Task<ListFundsResponse> ListFundsAsync(CancellationToken cancellation = default)
        {
            return SendCommandAsync<ListFundsResponse>("listfunds", cancellation: cancellation);
        }

        public async Task<PeerInfo[]> ListPeersAsync(CancellationToken cancellation = default)
        {
            var peers = await SendCommandAsync<PeerInfo[]>("listpeers", isArray: true, cancellation: cancellation);
            foreach (var peer in peers)
            {
                peer.Channels = peer.Channels ?? Array.Empty<ChannelInfo>();
            }
            return peers;
        }

        public Task FundChannelAsync(OpenChannelRequest openChannelRequest, CancellationToken cancellation)
        {
            OpenChannelRequest.AssertIsSane(openChannelRequest);
            List<object> parameters = new List<object>();
            parameters.Add(openChannelRequest.NodeInfo.NodeId.ToString());
            parameters.Add(openChannelRequest.ChannelAmount.Satoshi);
            if (openChannelRequest.FeeRate != null)
                parameters.Add($"{openChannelRequest.FeeRate.FeePerK.Satoshi * 4}perkw");
            return SendCommandAsync<object>("fundchannel", parameters.ToArray(), true, cancellation: cancellation);
        }

        public Task ConnectAsync(NodeInfo nodeInfo, CancellationToken cancellation = default)
        {
            return SendCommandAsync<object>("connect", new[] { $"{nodeInfo.NodeId}@{nodeInfo.Host}:{nodeInfo.Port}" }, true, cancellation: cancellation);
        }

        static Encoding UTF8 = new UTF8Encoding(false);
        internal async Task<T> SendCommandAsync<T>(string command, object[] parameters = null, bool noReturn = false, bool isArray = false, CancellationToken cancellation = default)
        {
            parameters = parameters ?? Array.Empty<string>();
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
                            req.Add("params", new JArray(parameters));
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
                                        throw new LightningRPCException(error.Value["message"].Value<string>(), error.Value["code"].Value<int>());
                                    }
                                    if (noReturn)
                                        return default;
                                    if (isArray)
                                    {
                                        return result["result"].Children().First().Children().First().ToObject<T>();
                                    }
                                    return result["result"].ToObject<T>();
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
            var addr = obj.ContainsKey("address") ? "address" : "bech32";
            return BitcoinAddress.Create(obj.Property(addr).Value.Value<string>(), Network);
        }

        public async Task<CLightningChannel[]> ListChannelsAsync(ShortChannelId ShortChannelId = null, CancellationToken cancellation = default)
        {
            var resp =
                ShortChannelId == null
                ? await SendCommandAsync<CLightningChannel[]>("listchannels", null, false, true, cancellation)
                : await SendCommandAsync<CLightningChannel[]>("listchannels", new[] { ShortChannelId.ToString() }, false, true, cancellation);

            return resp;
        }

        async Task<LightningPayment> ILightningClient.GetPayment(string paymentHash, CancellationToken cancellation)
        {
            return await GetPayment(paymentHash, cancellation);
        }

        async Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation)
        {
            var payments = await SendCommandAsync<CLightningPayment[]>("listpays", new[] { null, paymentHash }, false, true, cancellation);
            return payments.Length == 0 ? null : ToLightningPayment(payments.Last());
        }

        async Task<LightningInvoice> ILightningClient.GetInvoice(string invoiceId, CancellationToken cancellation)
        {
            var invoices = await SendCommandAsync<CLightningInvoice[]>("listinvoices", new[] { invoiceId }, false, true, cancellation);
            if (invoices.Length == 0 && invoiceId.Length == 64)
            {
                var paymentHash = new uint256(invoiceId);
                return await GetInvoice(paymentHash, cancellation);
            }
            return invoices.Length == 0 ? null : ToLightningInvoice(invoices[0]);
        }

        public async Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation)
        {
            var invoices = await SendCommandAsync<CLightningInvoice[]>("listinvoices", new[] { null, null, paymentHash.ToString() }, false, true, cancellation);
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

        private async Task<PayResponse> PayAsync(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            var isKeysend = bolt11 == null;
            if (isKeysend && payParams.Destination is null)
                throw new ArgumentNullException(nameof(payParams.Destination));
            if (isKeysend && payParams.Amount is null)
                throw new ArgumentNullException(nameof(payParams.Amount));
            
            bolt11 = bolt11?.Replace("lightning:", "").Replace("LIGHTNING:", "");
                
            // Pay the invoice - cancel after timeout, potentially caused by hold invoices
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var timeout = payParams?.SendTimeout ?? PayInvoiceParams.DefaultSendTimeout;
            cts.CancelAfter(timeout);
            
            try
            {
                var pr = bolt11 is null ? null : BOLT11PaymentRequest.Parse(bolt11, Network);
                // cln doesn't like using an explicit amount if it's the bolt's minimum amount for some reason
                var explicitAmount = pr?.MinimumAmount == payParams?.Amount ? null : payParams?.Amount;
                long? maxFeeFlat = payParams?.MaxFeeFlat is null ? null : new LightMoney(payParams?.MaxFeeFlat).MilliSatoshi;
                var feePercent = maxFeeFlat is null ? payParams?.MaxFeePercent : null;

                var command = isKeysend ? "keysend" : "pay";
                var opts = isKeysend
                    // keysend: destination msatoshi [label] [maxfeepercent] [retry_for] [maxdelay] [exemptfee] [extratlvs]
                    ? new object[] { payParams.Destination.ToHex(), explicitAmount.MilliSatoshi, null, feePercent }
                    // pay: bolt11 [msatoshi] [label] [riskfactor] [maxfeepercent] [retry_for] [maxdelay] [exemptfee] [localinvreqid] [exclude] [maxfee] [description]
                    : new object[] { bolt11, explicitAmount?.MilliSatoshi, null, null, feePercent, null, null, null, null, null, maxFeeFlat };
                var response = await SendCommandAsync<CLightningPayResponse>(command, opts, false, cancellation: cts.Token);

                return new PayResponse(PayResult.Ok, new PayDetails
                {
                    TotalAmount = response.AmountSent,
                    FeeAmount = response.AmountSent - response.Amount,
                    PaymentHash = response.PaymentHash,
                    Preimage = response.PaymentPreImage,
                    Status = ToPaymentStatus(response.Status)
                });
            }
            catch (LightningRPCException ex) when (
                // specific payment errors
                (ex.Code >= CLightningErrorCode.IN_PROGRESS && ex.Code <= CLightningErrorCode.STOPPED_RETRYING) ||
                // general know error codes
                ex.Code == CLightningErrorCode.WRONG_PARAMETERS || ex.Code == CLightningErrorCode.GENERAL_ERROR)
            {
                var routingError = ex.Code == CLightningErrorCode.ROUTE_NOT_FOUND ||
                                   (ex.Code == CLightningErrorCode.STOPPED_RETRYING && !ex.Message.Contains("invalid payload")) ||
                                   (ex.Code == CLightningErrorCode.WRONG_PARAMETERS && ex.Message.Contains("Self-payment"));
                var result =
                    routingError
                        ? PayResult.CouldNotFindRoute
                        : PayResult.Error;
                return new PayResponse(result, ex.Message);
            }
            catch (Exception ex) when (cts.Token.IsCancellationRequested && !cancellation.IsCancellationRequested)
            {
                if (bolt11 != null)
                {
                    var pr = BOLT11PaymentRequest.Parse(bolt11, Network);
                    var paymentHash = pr.PaymentHash?.ToString();
                    var response = await GetPayment(paymentHash, cancellation);
                    
                    switch (response.Status)
                    {
                        case LightningPaymentStatus.Unknown:
                        case LightningPaymentStatus.Pending:
                            return new PayResponse(PayResult.Unknown, ex.Message);

                        case LightningPaymentStatus.Failed:
                            return new PayResponse(PayResult.Error, ex.Message);
                        
                        case LightningPaymentStatus.Complete:
                            return new PayResponse(PayResult.Ok, new PayDetails
                            {
                                TotalAmount = response.AmountSent,
                                FeeAmount = response.Fee,
                                PaymentHash = new uint256(response.PaymentHash),
                                Preimage = new uint256(response.Preimage),
                                Status = response.Status
                            });
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
            return new PayResponse(PayResult.Unknown);
        }

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
            
            List<object> args = new List<object>();
            args.Add(msat);
            args.Add(id);
            args.Add(req.Description ?? "");
            args.Add(expiry);
            args.Add(null); // [fallbacks]
            args.Add(null); // [preimage]
            args.Add(req.PrivateRouteHints);

            bool usePlugin;
            if (req.DescriptionHashOnly)
            {
                args.Add(null); // [cltv]
                args.Add(true);
                usePlugin = false;
            }
            else
            {
                usePlugin = req.DescriptionHash is not null;
            }

            // Pre 22.11, we needed to use a plugin to support bolt11 with description hash.
            // This is not the case anymore, but we may fallback to using the plugin for old nodes.
            CLightningInvoice invoice = null;
            if (!usePlugin)
            {
                try
                {
                    invoice = await SendCommandAsync<CLightningInvoice>(
                        "invoice",
                        args.ToArray(),
                        cancellation: cancellation);
                }
                // Old nodes doesn't support descriptionHashOnly
                catch (LightningRPCException ex) when (req.DescriptionHashOnly && ex.Code == CLightningErrorCode.WRONG_PARAMETERS)
                {
                    // Remove two last parameters
                    args.RemoveAt(args.Count - 1);
                    args.RemoveAt(args.Count - 1);
                    usePlugin = true;
                }
            }

            if (usePlugin)
            {
                args[2] = req.DescriptionHash.ToString();
                invoice = await SendCommandAsync<CLightningInvoice>(
                        "invoicewithdescriptionhash",
                        args.ToArray(),
                        cancellation: cancellation);
            }

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
            await SendCommandAsync<CLightningInvoice>("delinvoice", new object[] { invoiceId, "unpaid" }, cancellation: cancellation);
        }

        async Task<LightningChannel[]> ILightningClient.ListChannels(CancellationToken cancellation)
        {
            var listPeersAsync = this.ListPeersAsync(cancellation);
            List<LightningChannel> channels = new List<LightningChannel>();
            foreach (var peer in await listPeersAsync)
            {
                foreach (var channel in peer.Channels)
                {
                    channels.Add(new LightningChannel
                    {
                        RemoteNode = new PubKey(peer.Id),
                        IsPublic = !channel.Private,
                        LocalBalance = channel.ToUs,
                        ChannelPoint = new OutPoint(channel.FundingTxId, channel.ShortChannelId.TxOutIndex),
                        Capacity = channel.Total,
                        IsActive = channel.State == "CHANNELD_NORMAL"
                    });
                }
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

        Task<ILightningInvoiceListener> ILightningClient.Listen(CancellationToken cancellation)
        {
            return Task.FromResult<ILightningInvoiceListener>(new CLightningInvoiceListener(this));
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
            return $"type=clightning;server={Address}";
        }
    }

    class CLightningInvoiceListener : ILightningInvoiceListener
    {
        CancellationTokenSource _Cts = new CancellationTokenSource();
        CLightningClient _Parent;
        public CLightningInvoiceListener(CLightningClient parent)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            _Parent = parent;
        }
        public void Dispose()
        {
            _Cts.Cancel();
        }

        long lastInvoiceIndex = 99999999999;

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
        {
            using (var cancellation2 = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _Cts.Token))
            {
                var invoice = await _Parent.SendCommandAsync<CLightningInvoice>("waitanyinvoice", new object[] { lastInvoiceIndex }, cancellation: cancellation2.Token);
                lastInvoiceIndex = invoice.PayIndex.Value;
                return CLightningClient.ToLightningInvoice(invoice);
            }
        }
    }
}
