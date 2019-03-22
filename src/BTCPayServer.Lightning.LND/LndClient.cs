using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json;
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
                if(str.EndsWith("/", StringComparison.InvariantCulture))
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
                    while(!_Cts.IsCancellationRequested)
                    {
                        string line = await WithCancellation(_Reader.ReadLineAsync(), _Cts.Token);
                        if(line != null)
                        {
                            if(line.StartsWith("{\"result\":", StringComparison.OrdinalIgnoreCase))
                            {
                                var invoiceString = JObject.Parse(line)["result"].ToString();
                                LnrpcInvoice parsedInvoice = _Parent.Deserialize<LnrpcInvoice>(invoiceString);
                                await _Invoices.Writer.WriteAsync(ConvertLndInvoice(parsedInvoice), _Cts.Token);
                            }
                            else if(line.StartsWith("{\"error\":", StringComparison.OrdinalIgnoreCase))
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
                catch when(_Cts.IsCancellationRequested)
                {

                }
                catch(Exception ex)
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
                using(var delayCTS = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var waiting = Task.Delay(-1, delayCTS.Token);
                    var doing = task;
                    await Task.WhenAny(waiting, doing);
                    delayCTS.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                    return await doing;
                }
            }

            public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
            {
                try
                {
                    return await _Invoices.Reader.ReadAsync(cancellation);
                }
                catch(ChannelClosedException ex) when(ex.InnerException == null)
                {
                    throw new OperationCanceledException();
                }
                catch(ChannelClosedException ex)
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
                if(_Cts.IsCancellationRequested)
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
                if(waitLoop)
                    _ListenLoop?.Wait();
                _Invoices.Writer.TryComplete();
            }
        }

        public LndClient(LndSwaggerClient swaggerClient, Network network)
        {
            if(swaggerClient == null)
                throw new ArgumentNullException(nameof(swaggerClient));
            if(network == null)
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

        async Task<LightningInvoice> ILightningClient.CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation)
        {
            var strAmount = ConvertInv.ToString(amount.ToUnit(LightMoneyUnit.Satoshi));
            var strExpiry = ConvertInv.ToString(Math.Round(expiry.TotalSeconds, 0));
            // lnd requires numbers sent as strings. don't ask
            var resp = await SwaggerClient.AddInvoiceAsync(new LnrpcInvoice
            {
                Value = strAmount,
                Memo = description,
                Expiry = strExpiry
            }, cancellation);

            var invoice = new LightningInvoice
            {
                Id = BitString(resp.R_hash),
                Amount = amount,
                BOLT11 = resp.Payment_request,
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = DateTimeOffset.UtcNow + expiry
            };
            return invoice;
        }

        async Task<LightningChannel[]> ILightningClient.ListChannels(CancellationToken token)
        {
            var resp = await this.SwaggerClient.ListChannelsAsync(false, false, false, false, token);
            if (resp.Channels == null)
                return new LightningChannel[] {};
            return (from c in resp.Channels
                    let tmp = c.Channel_point.Split(':')
                    let txHash = new uint256(tmp[0])
                    let outIndex = int.Parse(tmp[1])
                    select new LightningChannel() {
                        RemoteNode = new PubKey(c.Remote_pubkey),
                        IsPublic = !(c.Private ?? false) ,
                        IsActive = (c.Active == null ? false : c.Active.Value),
                        Capacity = c.Capacity,
                        LocalBalance = c.Local_balance,
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
                var node = await SwaggerClient.GetNodeInfoAsync(resp.Identity_pubkey, cancellation);
                var pubkey = new PubKey(resp.Identity_pubkey);
                var nodeInfos = node.Node.Addresses.Select(addr =>
                {
                    var externalHostPort = addr.Addr.Split(':');
                    return new NodeInfo(pubkey, externalHostPort[0], ConvertInv.ToInt32(externalHostPort[1]));
                }).ToList();
                if (nodeInfos.Count == 0)
                {
                    nodeInfos.Add(new NodeInfo(pubkey, "127.0.0.1", 9735));
                }
                nodeInfo.NodeInfoList = nodeInfos;
                return nodeInfo;
            }
            catch(SwaggerException ex) when(!string.IsNullOrEmpty(ex.Response))
            {
                throw new Exception("LND threw an error: " + ex.Response);
            }
        }

        async Task<LightningInvoice> ILightningClient.GetInvoice(string invoiceId, CancellationToken cancellation)
        {
            var resp = await SwaggerClient.LookupInvoiceAsync(invoiceId, null, cancellation);
            return ConvertLndInvoice(resp);
        }

        async Task<ILightningInvoiceListener> ILightningClient.Listen(CancellationToken cancellation)
        {
            var session = new LndInvoiceClientSession(this.SwaggerClient);
            await session.StartListening();
            return session;
        }

        internal static LightningInvoice ConvertLndInvoice(LnrpcInvoice resp)
        {
            var invoice = new LightningInvoice
            {
                // TODO: Verify id corresponds to R_hash
                Id = BitString(resp.R_hash),
                Amount = new LightMoney(ConvertInv.ToInt64(resp.Value), LightMoneyUnit.Satoshi),
                AmountReceived = string.IsNullOrWhiteSpace(resp.AmountPaid) ? null : new LightMoney(ConvertInv.ToInt64(resp.AmountPaid), LightMoneyUnit.MilliSatoshi),
                BOLT11 = resp.Payment_request,
                Status = LightningInvoiceStatus.Unpaid
            };
            invoice.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(ConvertInv.ToInt64(resp.Creation_date) + ConvertInv.ToInt64(resp.Expiry));
            if (resp.Settle_date != null)
            {
                invoice.PaidAt = DateTimeOffset.FromUnixTimeSeconds(ConvertInv.ToInt64(resp.Settle_date));
                
                invoice.Status = LightningInvoiceStatus.Paid;
            }
            else
            {
                if(invoice.ExpiresAt < DateTimeOffset.UtcNow)
                {
                    invoice.Status = LightningInvoiceStatus.Expired;
                }
            }
            return invoice;
        }


        // utility static methods... maybe move to separate class
        private static string BitString(byte[] bytes)
        {
            return BitConverter.ToString(bytes)
                .Replace("-", "")
                .ToLower(CultureInfo.InvariantCulture);
        }

        async Task<PayResponse> ILightningClient.Pay(string bolt11, CancellationToken cancellation)
        {
            retry:
            try
            {
                var response = await this.SwaggerClient.SendPaymentSyncAsync(new LnrpcSendRequest
                {
                    Payment_request = bolt11
                }, cancellation);

                if(response.Payment_error != null)
                {
                    return new PayResponse(PayResult.CouldNotFindRoute);
                }
            }
            catch(SwaggerException ex) when
                (ex.AsLNDError() is LndError2 lndError &&
                 lndError.Error.StartsWith("chain backend is still syncing"))
            {
                await Task.Delay(1000);
                goto retry;
            }
            return new PayResponse(PayResult.Ok);
        }




        async Task<OpenChannelResponse> ILightningClient.OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation)
        {
            OpenChannelRequest.AssertIsSane(openChannelRequest);
            retry:
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var req = new LnrpcOpenChannelRequest()
                {
                    Local_funding_amount = openChannelRequest.ChannelAmount.Satoshi.ToString(CultureInfo.InvariantCulture),
                    Node_pubkey_string = openChannelRequest.NodeInfo.NodeId.ToString(),
                };
                if(openChannelRequest.FeeRate != null)
                {
                    req.Sat_per_byte = ((int)openChannelRequest.FeeRate.SatoshiPerByte).ToString();
                }
                var result = await this.SwaggerClient.OpenChannelSyncAsync(req, cancellation);
                return new OpenChannelResponse(OpenChannelResult.Ok);
            }
            catch(SwaggerException ex) when
                (ex.AsLNDError() is LndError2 lndError &&
                 (lndError.Error.StartsWith("peer is not connected") ||
                 lndError.Error.EndsWith("is not online")))
            {
                return new OpenChannelResponse(OpenChannelResult.PeerNotConnected);
            }
            catch(SwaggerException ex) when
                (ex.AsLNDError() is LndError2 lndError &&
                 lndError.Error.StartsWith("not enough witness outputs"))
            {
                return new OpenChannelResponse(OpenChannelResult.CannotAffordFunding);
            }
            catch(SwaggerException ex) when
                (ex.AsLNDError() is LndError2 lndError &&
                 lndError.Code == 177)
            {
                var pendingChannels = await this.SwaggerClient.PendingChannelsAsync(cancellation);
                var nodePub = openChannelRequest.NodeInfo.NodeId.ToHex();
                if(pendingChannels.Pending_open_channels != null && 
                   pendingChannels.Pending_open_channels.Any(p => p.Channel.Remote_node_pub == nodePub))
                    return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);
                return new OpenChannelResponse(OpenChannelResult.AlreadyExists);
            }
            catch(SwaggerException ex) when
                (ex.AsLNDError() is LndError2 lndError &&
                 lndError.Error.StartsWith("channels cannot be created before"))
            {
                await Task.Delay(1000);
                goto retry;
            }
            catch(SwaggerException ex) when
                (ex.AsLNDError() is LndError2 lndError &&
                 lndError.Error.StartsWith("chain backend is still syncing"))
            {
                await Task.Delay(1000);
                goto retry;
            }
            catch(SwaggerException ex) when
                (ex.AsLNDError() is LndError2 lndError &&
                 lndError.Error.StartsWith("Number of pending channels exceed"))
            {
                return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);
            }
        }

        async Task<BitcoinAddress> ILightningClient.GetDepositAddress()
        {
            return BitcoinAddress.Create((await SwaggerClient.NewWitnessAddressAsync()).Address, Network);
        }

        async Task ILightningClient.ConnectTo(NodeInfo nodeInfo)
        {
            await SwaggerClient.ConnectPeerAsync(new LnrpcConnectPeerRequest()
            {
                Addr = new LnrpcLightningAddress()
                {
                    Host = nodeInfo.Host,
                    Pubkey = nodeInfo.NodeId.ToString()
                }
            });
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
