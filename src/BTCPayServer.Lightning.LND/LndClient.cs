using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.LND
{
    public class LndClient : ILightningClient
    {
        class LndInvoiceClientSession : ILightningInvoiceListener
        {
            private LndSwaggerClient _Parent;
            Channel<LightningInvoice> _Invoices = Channel.CreateBounded<LightningInvoice>(50);
            CancellationTokenSource _Cts = new CancellationTokenSource();
            HttpClient _Client;
            HttpResponseMessage _Response;
            Stream _Body;
            StreamReader _Reader;
            Task _ListenLoop;
            private readonly Action<string> _log;
            private const int MaxConsecutiveNullReads = 5;

            public LndInvoiceClientSession(LndSwaggerClient parent, Action<string> log)
            {
                _Parent = parent;
                _log = log ?? ((_) => { });
            }

            public Task StartListening()
            {
                try
                {
                    _Client = _Parent.CreateHttpClient();
                    _Client.Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite);
                    var request = new HttpRequestMessage(HttpMethod.Get, WithTrailingSlash(_Parent.BaseUrl) + "v1/invoices/subscribe");
                    _Parent._Authentication.AddAuthentication(request);
                    _ListenLoop = ListenLoop(request);
                }
                catch
                {
                    Dispose();
                }
                return Task.CompletedTask;
            }

            private string WithTrailingSlash(string str)
            {
                if (str.EndsWith("/", StringComparison.InvariantCulture))
                    return str;
                return str + "/";
            }

            private async Task ListenLoop(HttpRequestMessage request)
            {
                try
                {
                    _Response = await _Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _Cts.Token);
                    _Body = await _Response.Content.ReadAsStreamAsync();
                    _Reader = new StreamReader(_Body);
                    var consecutiveNullReads = 0;
                    while (!_Cts.IsCancellationRequested)
                    {
                        string line = await WithCancellation(_Reader.ReadLineAsync(), _Cts.Token);
                        if (line != null)
                        {
                            consecutiveNullReads = 0;
                            if (line.StartsWith("{\"result\":", StringComparison.OrdinalIgnoreCase))
                            {
                                var invoiceString = JObject.Parse(line)["result"].ToString();
                                LnrpcInvoice parsedInvoice = _Parent.Deserialize<LnrpcInvoice>(invoiceString);
                                await _Invoices.Writer.WriteAsync(ConvertLndInvoice(parsedInvoice), _Cts.Token);
                            }
                            else if (line.StartsWith("{\"error\":", StringComparison.OrdinalIgnoreCase))
                            {
                                var errorString = JObject.Parse(line)["error"].ToString();
                                var error = _Parent.Deserialize<LNDError>(errorString);
                                throw new LndException(error);
                            }
                            else
                            {
                                throw new LndException("Unknown result from LND: " + line);
                            }
                        }
                        else
                        {
                            consecutiveNullReads++;
                            _log($"LND invoice stream returned null (read #{consecutiveNullReads} of {MaxConsecutiveNullReads})");
                            if (consecutiveNullReads >= MaxConsecutiveNullReads)
                                break;
                        }
                    }
                }
                catch when (_Cts.IsCancellationRequested)
                {

                }
                catch (Exception ex)
                {
                    _Invoices.Writer.TryComplete(ex);
                }
                finally
                {
                    Dispose(false);
                }
            }

            public static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
            {
                using var delayCTS = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var waiting = Task.Delay(-1, delayCTS.Token);
                var doing = task;
                await Task.WhenAny(waiting, doing);
                delayCTS.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return await doing;
            }

            public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
            {
                try
                {
                    return await _Invoices.Reader.ReadAsync(cancellation);
                }
                catch (ChannelClosedException ex) when (ex.InnerException == null)
                {
                    throw new OperationCanceledException();
                }
                catch (ChannelClosedException ex)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }
            }

            public void Dispose()
            {
                Dispose(true);
            }
            void Dispose(bool waitLoop)
            {
                if (_Cts.IsCancellationRequested)
                    return;
                _Cts.Cancel();
                _Reader?.Dispose();
                _Reader = null;
                _Body?.Dispose();
                _Body = null;
                _Response?.Dispose();
                _Response = null;
                if (_Parent._DefaultHttpClient is null)
                    _Client?.Dispose();
                _Client = null;
                if (waitLoop)
                    _ListenLoop?.Wait();
                _Invoices.Writer.TryComplete();
            }
        }

        class LndPaymentClientSession : IDisposable
        {
            private LndSwaggerClient _Parent;
            Channel<LightningPayment> _Payments = Channel.CreateBounded<LightningPayment>(10);
            CancellationTokenSource _Cts = new CancellationTokenSource();
            HttpClient _Client;
            HttpResponseMessage _Response;
            Stream _Body;
            StreamReader _Reader;
            Task _ListenLoop;
            private readonly Func<HttpRequestMessage> _requestBuilder;
            private readonly Action<string> _log;
            private const int MaxConsecutiveNullReads = 5;

            // Set from the latest streamed payment result (routerrpc failure_reason enum name),
            // used by the sender to distinguish a missing route from other failures.
            public string LastFailureReason { get; private set; }

            // Tracks an existing payment: GET /v2/router/track/{payment_hash} (TrackPaymentV2).
            public LndPaymentClientSession(LndSwaggerClient parent, string paymentHash, Action<string> log)
            {
                _Parent = parent;
                _log = log ?? ((_) => { });
                _requestBuilder = () =>
                {
                    var hash = paymentHash.HexStringToBase64UrlString();
                    var request = new HttpRequestMessage(HttpMethod.Get, WithTrailingSlash(_Parent.BaseUrl) + $"v2/router/track/{hash}");
                    _Parent._Authentication.AddAuthentication(request);
                    return request;
                };
            }

            // Sends a payment: POST /v2/router/send (SendPaymentV2). This replaces the
            // lnrpc.SendPaymentSync (POST /v1/channels/transactions) endpoint that was
            // removed in LND 0.21.0.
            public LndPaymentClientSession(LndSwaggerClient parent, JObject sendRequest, Action<string> log)
            {
                _Parent = parent;
                _log = log ?? ((_) => { });
                _requestBuilder = () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, WithTrailingSlash(_Parent.BaseUrl) + "v2/router/send")
                    {
                        Content = new StringContent(sendRequest.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
                    };
                    _Parent._Authentication.AddAuthentication(request);
                    return request;
                };
            }

            public Task StartListening()
            {
                try
                {
                    _Client = _Parent.CreateHttpClient();
                    var request = _requestBuilder();
                    _ListenLoop = ListenLoop(request);
                }
                catch
                {
                    Dispose();
                }
                return Task.CompletedTask;
            }

            private string WithTrailingSlash(string str)
            {
                if (str.EndsWith("/", StringComparison.InvariantCulture))
                    return str;
                return str + "/";
            }

            private async Task ListenLoop(HttpRequestMessage request)
            {
                try
                {
                    _Response = await _Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _Cts.Token);
                    _Body = await _Response.Content.ReadAsStreamAsync();
                    _Reader = new StreamReader(_Body);
                    var consecutiveNullReads = 0;
                    while (!_Cts.IsCancellationRequested)
                    {
                        var line = await WithCancellation(_Reader.ReadLineAsync(), _Cts.Token);
                        if (line != null)
                        {
                            consecutiveNullReads = 0;
                            if (line.StartsWith("{\"result\":", StringComparison.OrdinalIgnoreCase))
                            {
                                var resultToken = JObject.Parse(line)["result"];
                                LastFailureReason = resultToken["failure_reason"]?.ToString();
                                LnrpcPayment parsed = _Parent.Deserialize<LnrpcPayment>(resultToken.ToString());
                                await _Payments.Writer.WriteAsync(ConvertLndPayment(parsed), _Cts.Token);
                            }
                            else if (line.StartsWith("{\"error\":", StringComparison.OrdinalIgnoreCase))
                            {
                                var errorString = JObject.Parse(line)["error"].ToString();
                                var error = _Parent.Deserialize<LNDError>(errorString);
                                throw new LndException(error);
                            }
                            else
                            {
                                throw new LndException("Unknown result from LND: " + line);
                            }
                        }
                        else
                        {
                            consecutiveNullReads++;
                            _log($"LND payment stream returned null (read #{consecutiveNullReads} of {MaxConsecutiveNullReads})");
                            if (consecutiveNullReads >= MaxConsecutiveNullReads)
                                break;
                        }
                    }
                }
                catch when (_Cts.IsCancellationRequested)
                {

                }
                catch (Exception ex)
                {
                    _Payments.Writer.TryComplete(ex);
                }
                finally
                {
                    Dispose(false);
                }
            }

            public static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
            {
                using var delayCTS = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var waiting = Task.Delay(-1, delayCTS.Token);
                var doing = task;
                await Task.WhenAny(waiting, doing);
                delayCTS.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return await doing;
            }

            public async Task<LightningPayment> WaitPayment(CancellationToken cancellation)
            {
                try
                {
                    return await _Payments.Reader.ReadAsync(cancellation);
                }
                catch (ChannelClosedException ex) when (ex.InnerException == null)
                {
                    throw new OperationCanceledException();
                }
                catch (ChannelClosedException ex)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }
            }

            public void Dispose()
            {
                Dispose(true);
            }
            void Dispose(bool waitLoop)
            {
                if (_Cts.IsCancellationRequested)
                    return;
                _Cts.Cancel();
                _Reader?.Dispose();
                _Reader = null;
                _Body?.Dispose();
                _Body = null;
                _Response?.Dispose();
                _Response = null;
                if (_Parent._DefaultHttpClient is null)
                    _Client?.Dispose();
                _Client = null;
                if (waitLoop)
                    _ListenLoop?.Wait();
                _Payments.Writer.TryComplete();
            }
        }

        public LndClient(LndSwaggerClient swaggerClient, Network network)
        {
            if (swaggerClient == null)
                throw new ArgumentNullException(nameof(swaggerClient));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            SwaggerClient = swaggerClient;
            Network = network;
        }
        public LndClient(LndRestSettings lndRestSettings, Network network) : this(new LndSwaggerClient(lndRestSettings), network)
        {

        }

        public Action<string> Log { get; set; }

        public Network Network
        {
            get;
        }
        public LndSwaggerClient SwaggerClient
        {
            get;
        }

        public Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation)
        {
            return CreateInvoice(new CreateInvoiceParams(amount, description, expiry), cancellation);
        }
        public async Task<LightningInvoice> CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation = default)
        {
            var strExpiry = ConvertInv.ToString(Math.Round(req.Expiry.TotalSeconds, 0));

            var lndRequest = new LnrpcInvoice
            {
                // null → field omitted from JSON (NullValueHandling.Ignore) → LND produces amountless bolt11
                ValueMSat = req.Amount == LightMoney.Zero
                    ? null
                    : ConvertInv.ToString(req.Amount.ToUnit(LightMoneyUnit.MilliSatoshi)),
                Memo = req.Description,
                Description_hash = req.DescriptionHash?.ToBytes(false),
                Expiry = strExpiry,
                Private = req.PrivateRouteHints
            };
            var resp = await SwaggerClient.AddInvoiceAsync(lndRequest, cancellation);

            var invoice = new LightningInvoice
            {
                Id = Encoders.Hex.EncodeData(resp.R_hash),
                Amount = req.Amount,
                BOLT11 = resp.Payment_request,
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = DateTimeOffset.UtcNow + req.Expiry,
                PaymentHash = new uint256(resp.R_hash, false).ToString()
            };
            return invoice;
        }

        public async Task CancelInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            var h = InvoiceIdToRHash(invoiceId);
            if (h is null)
                return;
            await SwaggerClient.CancelInvoiceAsync(new InvoicesrpcCancelInvoiceMsg
            {
                Payment_hash = h
            }, cancellation);
        }

        async Task<LightningChannel[]> ILightningClient.ListChannels(CancellationToken cancellation)
        {
            var resp = await SwaggerClient.ListChannelsAsync(false, false, false, false, cancellation);
            if (resp.Channels == null)
                return new LightningChannel[] { };
            return (from c in resp.Channels
                    let tmp = c.Channel_point.Split(':')
                    let txHash = new uint256(tmp[0])
                    let outIndex = int.Parse(tmp[1])
                    select new LightningChannel
                    {
                        RemoteNode = new PubKey(c.Remote_pubkey),
                        IsPublic = !(c.Private ?? false),
                        IsActive = c.Active ?? false,
                        Capacity = LightMoney.Satoshis(long.Parse(c.Capacity)),
                        LocalBalance = LightMoney.Satoshis(long.Parse(c.Local_balance)),
                        ChannelPoint = new OutPoint(txHash, outIndex)
                    }).ToArray();
        }

        async Task<LightningNodeInformation> ILightningClient.GetInfo(CancellationToken cancellation)
        {
            try
            {
                var resp = await SwaggerClient.GetInfoAsync(cancellation);

                var nodeInfo = new LightningNodeInformation
                {
                    BlockHeight = (int?)resp.Block_height ?? 0,
                    Alias = resp.Alias,
                    Color = resp.Color,
                    Version = resp.Version,
                    PeersCount = resp.Num_peers,
                    ActiveChannelsCount = resp.Num_active_channels,
                    InactiveChannelsCount = resp.Num_inactive_channels,
                    PendingChannelsCount = resp.Num_pending_channels
                };
                if (resp.Uris != null)
                {
                    foreach (var uri in resp.Uris)
                    {
                        if (NodeInfo.TryParse(uri, out var ni))
                            nodeInfo.NodeInfoList.Add(ni);
                    }
                }
                return nodeInfo;
            }
            catch (SwaggerException ex) when (ex.AsLNDError() is {} lndError)
            {
                if (lndError.Code == 2 || lndError.Error.StartsWith("permission denied"))
                {
                    throw new UnauthorizedAccessException(lndError.Error);
                }
                throw new LndException(lndError.Error);
            }
        }

        async Task<LightningNodeBalance> ILightningClient.GetBalance(CancellationToken cancellation)
        {
            var walletBalance = SwaggerClient.WalletBalanceAsync(cancellation);
            var channelBalance = SwaggerClient.ChannelBalanceAsync(cancellation);
            var pendingChannels = SwaggerClient.PendingChannelsAsync(cancellation);
            await Task.WhenAll(walletBalance, channelBalance, pendingChannels);

            var onchainResponse = walletBalance.Result;
            var offchainResponse = channelBalance.Result;
            var pendingResponse = pendingChannels.Result;
            
            var closing = new LightMoney(0);
            closing += pendingResponse.Pending_force_closing_channels.Sum(c => LightMoney.Satoshis(c.Limbo_balance));
            closing += pendingResponse.Waiting_close_channels.Sum(c => LightMoney.Satoshis(c.Limbo_balance));

            var onchain = new OnchainBalance
            { 
                Confirmed = string.IsNullOrEmpty(onchainResponse.Confirmed_balance) ? null : Money.Satoshis(long.Parse(onchainResponse.Confirmed_balance)),
                Unconfirmed = string.IsNullOrEmpty(onchainResponse.Unconfirmed_balance) ? null : Money.Satoshis(long.Parse(onchainResponse.Unconfirmed_balance)),
                Reserved = string.IsNullOrEmpty(onchainResponse.Locked_balance) ? null : Money.Satoshis(long.Parse(onchainResponse.Locked_balance))
            };
            var offchain = new OffchainBalance
            {
                Opening = new LightMoney(offchainResponse.Pending_open_local_balance.Msat),
                Local = new LightMoney(offchainResponse.Local_balance.Msat),
                Remote = new LightMoney(offchainResponse.Remote_balance.Msat),
                Closing = closing
            };
            
            return new LightningNodeBalance(onchain, offchain);
        }

        async Task<LightningInvoice> GetInvoice(byte[] invoiceId, CancellationToken cancellation)
        {
            try
            {
                var resp = await SwaggerClient.LookupInvoiceAsync(invoiceId, cancellation);
                return resp.State?.Equals("CANCELED", StringComparison.InvariantCultureIgnoreCase) is true ? null : ConvertLndInvoice(resp);
            }
            catch (SwaggerException ex) when
                (ex.StatusCode == "404")
            {
                return null;
            }
            catch (SwaggerException ex) when
               (ex.StatusCode == "500" && ex.AsLNDError() is LNDError err && err.Error.StartsWith("encoding/hex", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        async Task<LightningInvoice> ILightningClient.GetInvoice(string invoiceId, CancellationToken cancellation)
        {
            var h = InvoiceIdToRHash(invoiceId);
            if (h is null)
                return null;
            return await GetInvoice(h, cancellation);
        }
        byte[] InvoiceIdToRHash(string invoiceId)
        {
            try
            {
                var hash = Encoders.Hex.DecodeData(invoiceId);
                if (hash.Length != 32)
                    return null;
                return hash;
            }
            catch { return null; }
        }

        async Task<LightningInvoice> ILightningClient.GetInvoice(uint256 paymentHash, CancellationToken cancellation)
        {
            var invoiceId = paymentHash.ToBytes(false);
            return await GetInvoice(invoiceId, cancellation);
        }

        public async Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default)
        {
            return await ListInvoices(null, cancellation);
        }

        public async Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = default)
        {
            var resp = await SwaggerClient.ListInvoicesAsync(request?.PendingOnly, request?.OffsetIndex, cancellation);
            return resp.Invoices.Select(ConvertLndInvoice).ToArray();
        }

        async Task<LightningPayment> ILightningClient.GetPayment(string paymentHash, CancellationToken cancellation)
        {
            return await GetPayment(paymentHash, cancellation);
        }

        async Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation)
        {
            try
            {
                using var session = new LndPaymentClientSession(SwaggerClient, paymentHash, Log);
                await session.StartListening();
                var payment = await session.WaitPayment(cancellation);

                return payment;
            }
            catch (LndException ex) when (ex.Error is { Code: 5 } lndError)
            {
                return null;
            }
            catch (LndException ex) when (ex.Error is { Message: "payment isn't initiated" } lndError)
            {
                return null;
            }
        }

        public async Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default)
        {
            return await ListPayments(null, cancellation);
        }

        public async Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = default)
        {
            var resp = await SwaggerClient.ListPaymentsAsync(request?.IncludePending, null, cancellation);
            var payments = resp.Payments.Select(ConvertLndPayment).ToArray();
            if (request is { OffsetIndex: { } })
            {
                // we need to filter client-side, because the LNDs offset works differently
                payments = payments.Where(payment => 
                    !payment.CreatedAt.HasValue || payment.CreatedAt.Value.ToUnixTimeMilliseconds() >= request.OffsetIndex.Value).ToArray();
            }

            return payments;
        }

        async Task<ILightningInvoiceListener> ILightningClient.Listen(CancellationToken cancellation)
        {
            var session = new LndInvoiceClientSession(SwaggerClient, Log);
            await session.StartListening();
            return session;
        }

        private static LightningInvoice ConvertLndInvoice(LnrpcInvoice resp)
        {
            var invoice = new LightningInvoice
            {
                Id = Encoders.Hex.EncodeData(resp.R_hash),
                PaymentHash = new uint256(resp.R_hash, false).ToString(),
                Preimage = resp.R_preimage != null && resp.R_preimage.Length == 32 ? new uint256(resp.R_preimage, false).ToString() : null,
                Amount = new LightMoney(ConvertInv.ToInt64(resp.ValueMSat), LightMoneyUnit.MilliSatoshi),
                AmountReceived = string.IsNullOrWhiteSpace(resp.AmountPaid) ? null : new LightMoney(ConvertInv.ToInt64(resp.AmountPaid), LightMoneyUnit.MilliSatoshi),
                BOLT11 = resp.Payment_request,
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(ConvertInv.ToInt64(resp.Creation_date) + ConvertInv.ToInt64(resp.Expiry))
            };

            if (resp.Htlcs != null && resp.Htlcs.Any())
            {
                invoice.CustomRecords = resp.Htlcs
                    .Where(htlc => htlc.State.ToUpperInvariant() == "SETTLED")
                    .SelectMany(htlc => htlc.CustomRecords)
                    .GroupBy(htlc => htlc.Key)
                    .Select(x => x.First())
                    .ToDictionary(x => x.Key, y => y.Value);
            }
            
            if (resp.Settled == true)
            {
                invoice.PaidAt = DateTimeOffset.FromUnixTimeSeconds(ConvertInv.ToInt64(resp.Settle_date));
                invoice.Status = LightningInvoiceStatus.Paid;
            }
            else
            {
                if (invoice.ExpiresAt < DateTimeOffset.UtcNow)
                {
                    invoice.Status = LightningInvoiceStatus.Expired;
                }
            }
            return invoice;
        }

        private static LightningPayment ConvertLndPayment(LnrpcPayment resp)
        {
            var payment = new LightningPayment
            {
                PaymentHash = resp.Payment_hash,
                Preimage = resp.Payment_preimage,
                Fee = new LightMoney(ConvertInv.ToInt64(resp.FeeMSat), LightMoneyUnit.MilliSatoshi),
                Amount = new LightMoney(ConvertInv.ToInt64(resp.ValueMSat), LightMoneyUnit.MilliSatoshi),
                AmountSent = string.IsNullOrWhiteSpace(resp.ValueMSat) && string.IsNullOrWhiteSpace(resp.FeeMSat) ? null : new LightMoney(ConvertInv.ToInt64(resp.ValueMSat) + ConvertInv.ToInt64(resp.FeeMSat), LightMoneyUnit.MilliSatoshi),
                BOLT11 = resp.Payment_request,
                Status = LightningPaymentStatus.Unknown,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(ConvertInv.ToInt64(resp.Creation_date))
            };

            payment.Status = resp.Status switch
            {
                "INITIATED" => LightningPaymentStatus.Pending,
                "IN_FLIGHT" => LightningPaymentStatus.Pending,
                "SUCCEEDED" => LightningPaymentStatus.Complete,
                "FAILED" => LightningPaymentStatus.Failed,
                "UNKNOWN" => LightningPaymentStatus.Unknown,
                _ => payment.Status
            };

            return payment;
        }

        private async Task<PayResponse> PayAsync(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation)
        {
            // Pay the invoice - cancel after timeout, potentially caused by hold invoices
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var timeout = payParams?.SendTimeout ?? PayInvoiceParams.DefaultSendTimeout;
            cts.CancelAfter(timeout);

            var retryCount = 0;
retry:
            try
            {
                var sendRequest = BuildRouterSendRequest(bolt11, payParams, timeout);
                using var session = new LndPaymentClientSession(SwaggerClient, sendRequest, Log);
                await session.StartListening();
                var payment = await session.WaitPayment(cts.Token);

                switch (payment?.Status)
                {
                    case LightningPaymentStatus.Complete:
                        return new PayResponse(PayResult.Ok, new PayDetails
                        {
                            TotalAmount = payment.AmountSent,
                            FeeAmount = payment.Fee,
                            PaymentHash = string.IsNullOrEmpty(payment.PaymentHash) ? null : new uint256(payment.PaymentHash),
                            Preimage = string.IsNullOrEmpty(payment.Preimage) ? null : new uint256(payment.Preimage),
                            Status = LightningPaymentStatus.Complete
                        });
                    case LightningPaymentStatus.Failed:
                        return session.LastFailureReason switch
                        {
                            "FAILURE_REASON_NO_ROUTE" => new PayResponse(PayResult.CouldNotFindRoute, session.LastFailureReason),
                            "FAILURE_REASON_INSUFFICIENT_BALANCE" => new PayResponse(PayResult.CouldNotFindRoute, session.LastFailureReason),
                            null or "" or "FAILURE_REASON_NONE" => new PayResponse(PayResult.Error, "The payment failed"),
                            _ => new PayResponse(PayResult.Error, session.LastFailureReason)
                        };
                    default:
                        return new PayResponse(PayResult.Unknown);
                }
            }
            catch (LndException ex)
            {
                var message = ex.Message ?? string.Empty;
                if (message.IndexOf("already paid", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new PayResponse(PayResult.Ok);
                if (message.IndexOf("still syncing", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (retryCount++ > 3)
                        return new PayResponse(PayResult.Error, message);

                    await Task.Delay(1000, cancellation);
                    goto retry;
                }
                if (message.IndexOf("self-payment", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new PayResponse(PayResult.CouldNotFindRoute, message);
                if (message.IndexOf("in transition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("in flight", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new PayResponse(PayResult.Unknown, message);

                throw;
            }
            catch (Exception ex) when (cts.Token.IsCancellationRequested)
            {
                // The send stream was cancelled (our send timeout, e.g. a hold invoice that
                // never settles). The payment may still be in-flight, so resolve its real state.
                if (bolt11 != null)
                {
                    var pr = BOLT11PaymentRequest.Parse(bolt11, Network);
                    var paymentHash = pr.PaymentHash?.ToString();
                    var response = await GetPayment(paymentHash, cancellation);

                    switch (response?.Status)
                    {
                        case null:
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
                                Status = LightningPaymentStatus.Complete
                            });
                    }
                }
            }
            return new PayResponse(PayResult.Unknown);
        }

        // Builds the routerrpc.SendPaymentRequest body (JSON) for POST /v2/router/send.
        private JObject BuildRouterSendRequest(string bolt11, PayInvoiceParams payParams, TimeSpan timeout)
        {
            var req = new JObject();
            if (!string.IsNullOrEmpty(bolt11))
            {
                req["payment_request"] = bolt11;
            }
            else
            {
                // keysend payment
                req["dest"] = Encoders.Base64.EncodeData(payParams.Destination.ToBytes());
                req["payment_hash"] = Encoders.Base64.EncodeData(payParams.PaymentHash.ToBytes());
                if (payParams.CustomRecords is { Count: > 0 })
                {
                    var records = new JObject();
                    foreach (var rec in payParams.CustomRecords)
                        records[rec.Key.ToString(CultureInfo.InvariantCulture)] = rec.Value;
                    req["dest_custom_records"] = records;
                }
            }

            // routerrpc.SendPaymentV2 requires a payment attempt timeout; align it with the
            // client side send timeout so lnd and BTCPay give up at roughly the same time.
            req["timeout_seconds"] = Math.Max(1, (int)Math.Round(timeout.TotalSeconds));

            // routerrpc only supports an absolute fee limit (no percentage), so convert.
            long? feeLimitSat = null;
            if (payParams?.MaxFeePercent > 0)
            {
                var amount = payParams.Amount ?? BOLT11PaymentRequest.Parse(bolt11, Network).MinimumAmount;
                feeLimitSat = (long)(amount.ToDecimal(LightMoneyUnit.Satoshi) * (decimal)payParams.MaxFeePercent.Value / 100m);
            }
            if (payParams?.MaxFeeFlat?.Satoshi > 0)
                feeLimitSat = payParams.MaxFeeFlat.Satoshi;
            if (feeLimitSat is not null)
                req["fee_limit_sat"] = feeLimitSat.Value.ToString(CultureInfo.InvariantCulture);

            if (payParams?.Amount?.MilliSatoshi > 0)
                req["amt_msat"] = payParams.Amount.MilliSatoshi.ToString(CultureInfo.InvariantCulture);

            // We only need the terminal result, so suppress intermediate in-flight updates.
            req["no_inflight_updates"] = true;
            return req;
        }

        async Task<PayResponse> ILightningClient.Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation)
        {
            return await PayAsync(bolt11, payParams, cancellation);
        }

        async Task<PayResponse> ILightningClient.Pay(string bolt11, CancellationToken cancellation)
        {
            return await PayAsync(bolt11, null, cancellation);
        }

        async Task<PayResponse> ILightningClient.Pay(PayInvoiceParams payParams, CancellationToken cancellation)
        {
            return await PayAsync(null, payParams, cancellation);
        }

        //TODO: There is a bug here somewhere where we do not detect "requires funding channel message"
        async Task<OpenChannelResponse> ILightningClient.OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation)
        {
            OpenChannelRequest.AssertIsSane(openChannelRequest);
retry:
            int retryCount = 0;
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var req = new LnrpcOpenChannelRequest()
                {
                    Local_funding_amount = openChannelRequest.ChannelAmount.Satoshi.ToString(CultureInfo.InvariantCulture),
                    Node_pubkey_string = openChannelRequest.NodeInfo.NodeId.ToString(),
                };
                if (openChannelRequest.FeeRate != null)
                {
                    req.Sat_per_byte = ((int)openChannelRequest.FeeRate.SatoshiPerByte).ToString();
                }
                var result = await this.SwaggerClient.OpenChannelSyncAsync(req, cancellation);
                return new OpenChannelResponse(OpenChannelResult.Ok);
            }
            catch (SwaggerException ex) when
                (ex.AsLNDError() is LNDError lndError &&
                 (lndError.Error.StartsWith("peer is not connected") ||
                 lndError.Error.EndsWith("is not online")))
            {
                return new OpenChannelResponse(OpenChannelResult.PeerNotConnected);
            }
            catch (SwaggerException ex) when
                (ex.AsLNDError() is LNDError lndError &&
                 lndError.Error.StartsWith("not enough witness outputs"))
            {
                return new OpenChannelResponse(OpenChannelResult.CannotAffordFunding);
            }
            catch (SwaggerException ex) when
                (ex.AsLNDError() is LNDError lndError &&
                 lndError.Code == 177)
            {
                var pendingChannels = await this.SwaggerClient.PendingChannelsAsync(cancellation);
                var nodePub = openChannelRequest.NodeInfo.NodeId.ToHex();
                if (pendingChannels.Pending_open_channels != null &&
                   pendingChannels.Pending_open_channels.Any(p => p.Channel.Remote_node_pub == nodePub))
                    return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);
                return new OpenChannelResponse(OpenChannelResult.AlreadyExists);
            }
            catch (SwaggerException ex) when
                (ex.AsLNDError() is LNDError lndError &&
                 lndError.Error.StartsWith("channels cannot be created before"))
            {
                if (retryCount++ > 3)
                    return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);

                await Task.Delay(1000);
                goto retry;
            }
            catch (SwaggerException ex) when
                (ex.AsLNDError() is LNDError lndError &&
                 lndError.Error.StartsWith("chain backend is still syncing"))
            {
                if (retryCount++ > 3)
                    return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);

                await Task.Delay(1000);
                goto retry;
            }
            catch (SwaggerException ex) when
                (ex.AsLNDError() is LNDError lndError &&
                 lndError.Error.StartsWith("Number of pending channels exceed"))
            {
                return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);
            }
        }

        async Task<BitcoinAddress> ILightningClient.GetDepositAddress(CancellationToken cancellation)
        {
            return BitcoinAddress.Create((await SwaggerClient.NewWitnessAddressAsync(cancellation)).Address, Network);
        }

        async Task<ConnectionResult> ILightningClient.ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation)
        {
            return await SwaggerClient.ConnectPeer(new LnrpcConnectPeerRequest
            {
                Addr = new LnrpcLightningAddress()
                {
                    Host = $"{nodeInfo.Host}:{nodeInfo.Port}",
                    Pubkey = nodeInfo.NodeId.ToString()
                }
            }, cancellation);
        }

        // Invariant culture conversion
        public static class ConvertInv
        {
            public static int ToInt32(string str)
            {
                return Convert.ToInt32(str, CultureInfo.InvariantCulture.NumberFormat);
            }

            public static long ToInt64(string str)
            {
                return Convert.ToInt64(str, CultureInfo.InvariantCulture.NumberFormat);
            }

            public static string ToString(decimal d)
            {
                return d.ToString(CultureInfo.InvariantCulture);
            }

            public static string ToString(double d)
            {
                return d.ToString(CultureInfo.InvariantCulture);
            }
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append($"type=lnd-rest;server={SwaggerClient._LndSettings.Uri}");
            if (SwaggerClient._LndSettings.Macaroon != null)
            {
                builder.Append($";macaroon={ConvertHelper.ToHexString(SwaggerClient._LndSettings.Macaroon)}");
            }
            if (SwaggerClient._LndSettings.MacaroonFilePath != null)
            {
                builder.Append($";macaroonfilepath={SwaggerClient._LndSettings.MacaroonFilePath}");
            }
            if (SwaggerClient._LndSettings.MacaroonDirectoryPath != null)
            {
                builder.Append($";macaroondirectorypath={SwaggerClient._LndSettings.MacaroonDirectoryPath}");
            }
            if (SwaggerClient._LndSettings.CertificateThumbprint != null)
            {
                builder.Append($";certthumbprint={ConvertHelper.ToHexString(SwaggerClient._LndSettings.CertificateThumbprint)}");
            }
            if (SwaggerClient._LndSettings.CertificateFilePath != null)
            {
                builder.Append($";certfilepath={SwaggerClient._LndSettings.CertificateFilePath}");
            }
            if (SwaggerClient._LndSettings.AllowInsecure)
            {
                builder.Append($";allowinsecure=true");
            }

            return builder.ToString();
        }
    }
}
