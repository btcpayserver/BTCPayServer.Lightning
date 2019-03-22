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
using NBitcoin.RPC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.CLightning
{
    public class LightningRPCException : Exception
    {
        public LightningRPCException(string message, int code) : base(message)
        {
            ErrorCode = code;
        }

        public int ErrorCode
        {
            get;
        }
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
            if(address == null)
                throw new ArgumentNullException(nameof(address));
            if(network == null)
                throw new ArgumentNullException(nameof(network));
            if(address.Scheme == "file")
            {
                address = new UriBuilder(address) { Scheme = "unix" }.Uri;
            }
            Address = address;
            Network = network;
        }

        public Task<GetInfoResponse> GetInfoAsync(CancellationToken cancellation = default(CancellationToken))
        {
            return SendCommandAsync<GetInfoResponse>("getinfo", cancellation: cancellation);
        }

        public Task SendAsync(string bolt11, CancellationToken cancellationToken)
        {
            if (bolt11 == null)
                throw new ArgumentNullException(nameof(bolt11));
            bolt11 = bolt11.Replace("lightning:", "").Replace("LIGHTNING:", "");
            return SendCommandAsync<object>("pay", new[] { bolt11 }, true, cancellation: cancellationToken);
        }

        public async Task<PeerInfo[]> ListPeersAsync(CancellationToken cancellation = default(CancellationToken))
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
            if(openChannelRequest.FeeRate != null)
                parameters.Add($"{openChannelRequest.FeeRate.FeePerK.Satoshi * 4}perkw");
            return SendCommandAsync<object>("fundchannel", parameters.ToArray(), true, cancellation: cancellation);
        }

        public Task ConnectAsync(NodeInfo nodeInfo)
        {
            return SendCommandAsync<object>("connect", new[] { $"{nodeInfo.NodeId}@{nodeInfo.Host}:{nodeInfo.Port}" }, true);
        }

        static Encoding UTF8 = new UTF8Encoding(false);
        internal async Task<T> SendCommandAsync<T>(string command, object[] parameters = null, bool noReturn = false, bool isArray = false, CancellationToken cancellation = default(CancellationToken))
        {
            parameters = parameters ?? Array.Empty<string>();
            using(Socket socket = await Connect())
            {
                using(var networkStream = new NetworkStream(socket))
                {
                    using(var textWriter = new StreamWriter(networkStream, UTF8, 1024 * 10, true))
                    {
                        using(var jsonWriter = new JsonTextWriter(textWriter))
                        {
                            var req = new JObject();
                            req.Add("id", 0);
                            req.Add("method", command);
                            req.Add("params", new JArray(parameters));
                            await req.WriteToAsync(jsonWriter, cancellation);
                            await jsonWriter.FlushAsync(cancellation);
                        }
                        await textWriter.FlushAsync();
                    }
                    await networkStream.FlushAsync(cancellation);
                    using(var textReader = new StreamReader(networkStream, UTF8, false, 1024 * 10, true))
                    {
                        using(var jsonReader = new JsonTextReader(textReader))
                        {
                            var resultAsync = JObject.LoadAsync(jsonReader, cancellation);

                            // without this hack resultAsync is blocking even if cancellation happen
                            using(cancellation.Register(() =>
                            {
                                socket.Dispose();
                            }))
                            {
                                try
                                {

                                    var result = await resultAsync;
                                    var error = result.Property("error");
                                    if(error != null)
                                    {
                                        throw new LightningRPCException(error.Value["message"].Value<string>(), error.Value["code"].Value<int>());
                                    }
                                    if(noReturn)
                                        return default(T);
                                    if(isArray)
                                    {
                                        return result["result"].Children().First().Children().First().ToObject<T>();
                                    }
                                    return result["result"].ToObject<T>();
                                }
                                catch when(cancellation.IsCancellationRequested)
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
            if(Address.Scheme == "tcp" || Address.Scheme == "tcp")
            {
                var domain = Address.DnsSafeHost;
                if(!IPAddress.TryParse(domain, out IPAddress address))
                {
                    address = (await Dns.GetHostAddressesAsync(domain)).FirstOrDefault();
                    if(address == null)
                        throw new Exception("Host not found");
                }
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                endpoint = new IPEndPoint(address, Address.Port);
            }
            else if(Address.Scheme == "unix")
            {
                var path = Address.AbsoluteUri.Remove(0, "unix:".Length);
                if(!path.StartsWith("/"))
                    path = "/" + path;
                while(path.Length >= 2 && (path[0] != '/' || path[1] == '/'))
                {
                    path = path.Remove(0, 1);
                }
                if(path.Length < 2)
                    throw new FormatException("Invalid unix url");
                socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                endpoint = new UnixEndPoint(path);
            }
            else
                throw new NotSupportedException($"Protocol {Address.Scheme} for clightning not supported");

            await socket.ConnectAsync(endpoint);
            return socket;
        }

        public async Task<BitcoinAddress> NewAddressAsync()
        {
            var obj = await SendCommandAsync<JObject>("newaddr");
            return BitcoinAddress.Create(obj.Property("address").Value.Value<string>(), Network);
        }

        public async Task<CLightningChannel[]> ListChannelsAsync(ShortChannelId ShortChannelId = null, CancellationToken cancellation = default(CancellationToken))
        {
            var resp =
                ShortChannelId == null
                ? await SendCommandAsync<CLightningChannel[]>("listchannels", null, false, true, cancellation)
                : await SendCommandAsync<CLightningChannel[]>("listchannels", new[] { ShortChannelId.ToString() }, false, true, cancellation);

            return resp;
        }

        async Task<LightningInvoice> ILightningClient.GetInvoice(string invoiceId, CancellationToken cancellation)
        {
            var invoices = await SendCommandAsync<CLightningInvoice[]>("listinvoices", new[] { invoiceId }, false, true, cancellation);
            if (invoices.Length == 0)
                return null;
            return ToLightningInvoice(invoices[0]);
        }

        async Task<PayResponse> ILightningClient.Pay(string bolt11, CancellationToken cancellation)
        {
            try
            {
                await SendAsync(bolt11, cancellation);
                return new PayResponse(PayResult.Ok);
            }
            catch(LightningRPCException ex) when(ex.ErrorCode == 205)
            {
                return new PayResponse(PayResult.CouldNotFindRoute);
            }
        }

        static NBitcoin.DataEncoders.DataEncoder InvoiceIdEncoder = NBitcoin.DataEncoders.Encoders.Base58;
        async Task<LightningInvoice> ILightningClient.CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation)
        {
            var id = InvoiceIdEncoder.EncodeData(RandomUtils.GetBytes(20));
            var invoice = await SendCommandAsync<CLightningInvoice>("invoice", new object[] { amount.MilliSatoshi, id, description ?? "", Math.Max(0, (int)expiry.TotalSeconds) }, cancellation: cancellation);
            invoice.Label = id;
            invoice.MilliSatoshi = amount;
            invoice.Status = "unpaid";
            return ToLightningInvoice(invoice);
        }

        async Task ILightningClient.ConnectTo(NodeInfo nodeInfo)
        {
            await ConnectAsync(nodeInfo);
        }

        async Task<LightningChannel[]> ILightningClient.ListChannels(CancellationToken cancellation)
        {
            // Since `channels` in listpeers does not contain info about IsActive or not, we need to query listChannels as well
            var listPeersAsync = this.ListPeersAsync(cancellation);
            var channels = await this.ListChannelsAsync(cancellation: cancellation);
            return (from p in await listPeersAsync
                    from c1 in p.Channels
                    let c2 = channels.Where(c => (c.Source == p.Id)).FirstOrDefault()
                    where c2 != null
                    select new LightningChannel()
                    {
                        RemoteNode = new PubKey(p.Id),
                        IsPublic = !c1.Private,
                        IsActive = c2.Active,
                        Capacity = c2.Capacity,
                        LocalBalance = c1.ToUs,
                        ChannelPoint = new OutPoint(c1.FundingTxId, c1.ShortChannelId.TxOutIndex)
                    }
                   ).ToArray();
        }

        internal static LightningInvoice ToLightningInvoice(CLightningInvoice invoice)
        {
            return new LightningInvoice()
            {
                Id = invoice.Label,
                Amount = invoice.MilliSatoshi,
                AmountReceived = invoice.MilliSatoshiReceived,
                BOLT11 = invoice.BOLT11,
                Status = ToStatus(invoice.Status),
                PaidAt = invoice.PaidAt,
                ExpiresAt = invoice.ExpiryAt
            };
        }

        public static LightningInvoiceStatus ToStatus(string status)
        {
            switch(status)
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

        Task<ILightningInvoiceListener> ILightningClient.Listen(CancellationToken cancellation)
        {
            return Task.FromResult<ILightningInvoiceListener>(new CLightningInvoiceListener(this));
        }

        async Task<LightningNodeInformation> ILightningClient.GetInfo(CancellationToken cancellation)
        {
            var info = await GetInfoAsync(cancellation);
            return ToLightningNodeInformation(info);
        }

        async Task<OpenChannelResponse> ILightningClient.OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation)
        {
            try
            {
                await FundChannelAsync(openChannelRequest, cancellation);
            }
            catch(LightningRPCException ex) when(ex.ErrorCode == 301)
            {
                return new OpenChannelResponse(OpenChannelResult.CannotAffordFunding);
            }
            catch(LightningRPCException ex) when(ex.Message == "Peer not connected" || ex.Message == "Unknown peer")
            {
                return new OpenChannelResponse(OpenChannelResult.PeerNotConnected);
            }
            catch(LightningRPCException ex) when(ex.Message.Contains("CHANNELD_AWAITING_LOCKIN"))
            {
                return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);
            }
            catch(LightningRPCException ex) when(
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

        async Task<BitcoinAddress> ILightningClient.GetDepositAddress()
        {
            return await this.NewAddressAsync();
        }

        public static LightningNodeInformation ToLightningNodeInformation(GetInfoResponse info)
        {
            var pubkey = new PubKey(info.Id);
            var nodeInfos = info.Address.Select(addr => new NodeInfo(pubkey, addr.Address, addr.Port == 0 ? 9735 : addr.Port)).ToList();
            if (nodeInfos.Count == 0)
            {
                nodeInfos.Add(new NodeInfo(pubkey, "127.0.0.1", 9735));
            }
            return new LightningNodeInformation()
            {
                NodeInfoList = nodeInfos,
                BlockHeight = info.BlockHeight
            };
        }
    }

    class CLightningInvoiceListener : ILightningInvoiceListener
    {
        CancellationTokenSource _Cts = new CancellationTokenSource();
        CLightningClient _Parent;
        public CLightningInvoiceListener(CLightningClient parent)
        {
            if(parent == null)
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
            using(var cancellation2 = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _Cts.Token))
            {
                var invoice = await _Parent.SendCommandAsync<CLightningInvoice>("waitanyinvoice", new object[] { lastInvoiceIndex }, cancellation: cancellation2.Token);
                lastInvoiceIndex = invoice.PayIndex.Value;
                return CLightningClient.ToLightningInvoice(invoice);
            }
        }
    }
}
