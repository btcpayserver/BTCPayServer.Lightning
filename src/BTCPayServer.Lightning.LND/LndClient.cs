using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NBitcoin;
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

            public LndInvoiceClientSession(LndSwaggerClient parent)
            {
                _Parent = parent;
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
                    while (!_Cts.IsCancellationRequested)
                    {
                        string line = await WithCancellation(_Reader.ReadLineAsync(), _Cts.Token);
                        if (line != null)
                        {
                            if (line.StartsWith("{\"result\":", StringComparison.OrdinalIgnoreCase))
                            {
                                var invoiceString = JObject.Parse(line)["result"].ToString();
                                LnrpcInvoice parsedInvoice = _Parent.Deserialize<LnrpcInvoice>(invoiceString);
                                await _Invoices.Writer.WriteAsync(ConvertLndInvoice(parsedInvoice), _Cts.Token);
                            }
                            else if (line.StartsWith("{\"error\":", StringComparison.OrdinalIgnoreCase))
                            {
                                var errorString = JObject.Parse(line)["error"].ToString();
                                var error = _Parent.Deserialize<LndError>(errorString);
                                throw new LndException(error);
                            }
                            else
                            {
                                throw new LndException("Unknown result from LND: " + line);
                            }
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
            private readonly string _PaymentHash;

            public LndPaymentClientSession(LndSwaggerClient parent, string paymentHash)
            {
                _Parent = parent;
                _PaymentHash = paymentHash;
            }

            public Task StartListening()
            {
                try
                {
                    _Client = _Parent.CreateHttpClient();
                    _Client.Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite);
                    var paymentHash = _PaymentHash.HexStringToBase64UrlString();
                    var request = new HttpRequestMessage(HttpMethod.Get, WithTrailingSlash(_Parent.BaseUrl) + $"v2/router/track/{paymentHash}");
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
                    while (!_Cts.IsCancellationRequested)
                    {
                        var line = await WithCancellation(_Reader.ReadLineAsync(), _Cts.Token);
                        if (line != null)
                        {
                            if (line.StartsWith("{\"result\":", StringComparison.OrdinalIgnoreCase))
                            {
                                var paymentString = JObject.Parse(line)["result"].ToString();
                                LnrpcPayment parsed = _Parent.Deserialize<LnrpcPayment>(paymentString);
                                await _Payments.Writer.WriteAsync(ConvertLndPayment(parsed), _Cts.Token);
                            }
                            else if (line.StartsWith("{\"error\":", StringComparison.OrdinalIgnoreCase))
                            {
                                var errorString = JObject.Parse(line)["error"].ToString();
                                var error = _Parent.Deserialize<LndError>(errorString);
                                throw new LndException(error);
                            }
                            else
                            {
                                throw new LndException("Unknown result from LND: " + line);
                            }
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
            var strAmount = ConvertInv.ToString(req.Amount.ToUnit(LightMoneyUnit.MilliSatoshi));
            var strExpiry = ConvertInv.ToString(Math.Round(req.Expiry.TotalSeconds, 0));

            var lndRequest = new LnrpcInvoice
            {
                ValueMSat = strAmount,
                Memo = req.Description,
                Description_hash = req.DescriptionHash?.ToBytes(false),
                Expiry = strExpiry,
                Private = req.PrivateRouteHints
            };
            var resp = await SwaggerClient.AddInvoiceAsync(lndRequest, cancellation);

            var invoice = new LightningInvoice
            {
                Id = BitString(resp.R_hash),
                Amount = req.Amount,
                BOLT11 = resp.Payment_request,
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = DateTimeOffset.UtcNow + req.Expiry
            };
            return invoice;
        }

        public async Task CancelInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            var resp = await SwaggerClient.LookupInvoiceAsync(invoiceId, null, cancellation);
            await SwaggerClient.CancelInvoiceAsync(new InvoicesrpcCancelInvoiceMsg
            {
                Payment_hash = resp.R_hash
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
            var resp = await SwaggerClient.GetInfoAsync(cancellation);

            var nodeInfo = new LightningNodeInformation
            {
                BlockHeight = (int?)resp.Block_height ?? 0,
            };

            try
            {
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
            catch (SwaggerException ex) when (!string.IsNullOrEmpty(ex.Response))
            {
                throw new Exception("LND threw an error: " + ex.Response);
            }
        }

        async Task<LightningInvoice> ILightningClient.GetInvoice(string invoiceId, CancellationToken cancellation)
        {
            try
            {
                var resp = await SwaggerClient.LookupInvoiceAsync(invoiceId, null, cancellation);
                return resp.State?.Equals("CANCELED", StringComparison.InvariantCultureIgnoreCase) is true ? null : ConvertLndInvoice(resp);
            }
            catch (SwaggerException ex) when
                (ex.StatusCode == "404")
            {
                return null;
            }
            catch (SwaggerException ex) when
               (ex.StatusCode == "500" && ex.AsLNDError() is LndError2 err && err.Error.StartsWith("encoding/hex", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        async Task<LightningPayment> ILightningClient.GetPayment(string paymentHash, CancellationToken cancellation)
        {
            try
            {
                using var session = new LndPaymentClientSession(SwaggerClient, paymentHash);
                await session.StartListening();
                var payment = await session.WaitPayment(cancellation);

                return payment;
            }
            catch (SwaggerException ex)
            {
                var errorString = JObject.Parse(ex.Response)["error"]["message"].ToString();
                throw new LndException(errorString);
            }
        }

        async Task<ILightningInvoiceListener> ILightningClient.Listen(CancellationToken cancellation)
        {
            var session = new LndInvoiceClientSession(SwaggerClient);
            await session.StartListening();
            return session;
        }

        private static LightningInvoice ConvertLndInvoice(LnrpcInvoice resp)
        {
            var invoice = new LightningInvoice
            {
                // TODO: Verify id corresponds to R_hash
                Id = BitString(resp.R_hash),
                Amount = new LightMoney(ConvertInv.ToInt64(resp.ValueMSat), LightMoneyUnit.MilliSatoshi),
                AmountReceived = string.IsNullOrWhiteSpace(resp.AmountPaid) ? null : new LightMoney(ConvertInv.ToInt64(resp.AmountPaid), LightMoneyUnit.MilliSatoshi),
                BOLT11 = resp.Payment_request,
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(ConvertInv.ToInt64(resp.Creation_date) + ConvertInv.ToInt64(resp.Expiry))
            };
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

            switch (resp.Status)
            {
                case "IN_FLIGHT":
                    payment.Status = LightningPaymentStatus.Pending;
                    break;
                case "SUCCEEDED":
                    payment.Status = LightningPaymentStatus.Complete;
                    break;
                case "FAILED":
                    payment.Status = LightningPaymentStatus.Failed;
                    break;
                case "UNKNOWN":
                    payment.Status = LightningPaymentStatus.Unknown;
                    break;
            }

            return payment;
        }

        // utility static methods... maybe move to separate class
        private static string BitString(byte[] bytes)
        {
            return BitConverter.ToString(bytes)
                .Replace("-", "")
                .ToLower(CultureInfo.InvariantCulture);
        }

        private async Task<PayResponse> PayAsync(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation)
        {
retry:
            var retryCount = 0;
            try
            {
                var req = new LnrpcSendRequest { Payment_request = bolt11 };
                if (payParams?.MaxFeePercent > 0)
                {
                    req.Fee_limit ??= new LnrpcFeeLimit();
                    if (payParams.MaxFeePercent.Value < 1.0) // doesn't support sub 1% fee, so we calculate ourself
                    {
                        var satValue = BOLT11PaymentRequest.Parse(bolt11, Network).MinimumAmount.ToDecimal(LightMoneyUnit.Satoshi);
                        req.Fee_limit.Fixed = (long)((satValue * (decimal)payParams.MaxFeePercent.Value) / 100m);
                    }
                    else
                        req.Fee_limit.Percent = ((int)Math.Round(payParams.MaxFeePercent.Value));
                }
                if (payParams?.MaxFeeFlat?.Satoshi > 0)
                {
                    req.Fee_limit ??= new LnrpcFeeLimit();
                    req.Fee_limit.Fixed = payParams.MaxFeeFlat.Satoshi;
                }
                if (payParams?.Amount?.MilliSatoshi > 0)
                {
                    req.AmtMsat = payParams.Amount.MilliSatoshi.ToString();
                }

                var response = await SwaggerClient.SendPaymentSyncAsync(req, cancellation);
                if (string.IsNullOrEmpty(response.Payment_error) && response.Payment_preimage != null)
                {
                    if (response.Payment_route != null)
                    {
                        return new PayResponse(PayResult.Ok, new PayDetails
                        {
                            TotalAmount = new LightMoney(response.Payment_route.Total_amt_msat),
                            FeeAmount = new LightMoney(response.Payment_route.Total_fees_msat)
                        });
                    }

                    return new PayResponse(PayResult.Ok);
                }

                switch (response.Payment_error)
                {
                    case "invoice is already paid":
                        return new PayResponse(PayResult.Ok);
                    case "insufficient local balance":
                    case "unable to find a path to destination":
                    // code in 0.10.0+
                    case "insufficient_balance":
                    case "no_route":
                        return new PayResponse(PayResult.CouldNotFindRoute, response.Payment_error);
                    default:
                        return new PayResponse(PayResult.Error, response.Payment_error);
                }
            }
            catch (SwaggerException ex) when
                (ex.AsLNDError() is LndError2 lndError)
            {
                if (lndError.Error.StartsWith("chain backend is still syncing"))
                {
                    if (retryCount++ > 3)
                        return new PayResponse(PayResult.Error, ex.Response);

                    await Task.Delay(1000, cancellation);
                    goto retry;
                }
                if (lndError.Error.StartsWith("self-payments not allowed"))
                {
                    return new PayResponse(PayResult.CouldNotFindRoute, ex.Response);
                }

                throw new LndException(lndError.Error);
            }
        }

        async Task<PayResponse> ILightningClient.Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation)
        {
            return await PayAsync(bolt11, payParams, cancellation);
        }

        async Task<PayResponse> ILightningClient.Pay(string bolt11, CancellationToken cancellation)
        {
            return await PayAsync(bolt11, null, cancellation);
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
                (ex.AsLNDError() is LndError2 lndError &&
                 (lndError.Error.StartsWith("peer is not connected") ||
                 lndError.Error.EndsWith("is not online")))
            {
                return new OpenChannelResponse(OpenChannelResult.PeerNotConnected);
            }
            catch (SwaggerException ex) when
                (ex.AsLNDError() is LndError2 lndError &&
                 lndError.Error.StartsWith("not enough witness outputs"))
            {
                return new OpenChannelResponse(OpenChannelResult.CannotAffordFunding);
            }
            catch (SwaggerException ex) when
                (ex.AsLNDError() is LndError2 lndError &&
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
                (ex.AsLNDError() is LndError2 lndError &&
                 lndError.Error.StartsWith("channels cannot be created before"))
            {
                if (retryCount++ > 3)
                    return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);

                await Task.Delay(1000);
                goto retry;
            }
            catch (SwaggerException ex) when
                (ex.AsLNDError() is LndError2 lndError &&
                 lndError.Error.StartsWith("chain backend is still syncing"))
            {
                if (retryCount++ > 3)
                    return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);

                await Task.Delay(1000);
                goto retry;
            }
            catch (SwaggerException ex) when
                (ex.AsLNDError() is LndError2 lndError &&
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
    }
}
