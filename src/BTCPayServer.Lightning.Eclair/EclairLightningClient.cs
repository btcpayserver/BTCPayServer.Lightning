using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Eclair.Models;
using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using WebSocketSharp;

namespace BTCPayServer.Lightning.Eclair
{
    public class EclairClient()
    {
        private Uri _Address;
        private HttpClient _httpClient;


        public EclairClient(Uri address, string password)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));

            _Address = address;

            _httpClient = new HttpClient();
            _httpClient.BaseAddress = address;
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.Default.GetBytes($":{password}")));
        }

        public async Task<GetInfoResponse> GetInfo(CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<GetInfoResponse>("getinfo", cts);
        }

        public async Task<string> ConnectToNode(string nodeId, string host, int port,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<string>("connect", cts, nodeId, host, port);
        }

        public async Task<string> ConnectToNode(string uri, CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<string>("connect", cts, uri);
        }

        public async Task<string> OpenChannel(string nodeId, long fundingSatoshis, long pushMsat = 0,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<string>("open", cts, nodeId, fundingSatoshis, pushMsat);
        }

        public async Task<string> OpenChannel(string nodeId, long fundingSatoshis, long pushMsat,
            long feerateSatPerByte, CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<string>("open", cts, nodeId, fundingSatoshis, pushMsat, feerateSatPerByte);
        }

        public async Task<string> UpdateRelayFee(string channelId, long feeBaseMsat, long feeProportionalMillionths,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<string>("updaterelayfee", cts, channelId, feeBaseMsat,
                feeProportionalMillionths);
        }

        public async Task<ListChannelsResponseItem[]> ListChannels(CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<ListChannelsResponseItem[]>("channels", cts);
        }

        public async Task<ListChannelsResponseItem[]> ListChannels(string nodeId,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<ListChannelsResponseItem[]>("channels", cts, nodeId);
        }

        public async Task<ListChannelsResponseItem> GetChannel(string channelId,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<ListChannelsResponseItem>("channel", cts, channelId);
        }

        public async Task<ListChannelsResponseItem> ListAllChannels(CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<ListChannelsResponseItem>("allchannels", cts);
        }

        public async Task<NodeResult[]> ListAllNodes(CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<NodeResult[]>("allnodes", cts);
        }

        public async Task<string> Receive(string description, CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<string>("receive", cts, description);
        }

        public async Task<string> Receive(string description, long mSat,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<string>("receive", cts, mSat, description);
        }

        public async Task<string> Receive(string description, long mSat, int expirySeconds,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<string>("receive", cts,
                mSat,
                description,
                expirySeconds);
        }

        public async Task<SendResponse> Send(string paymentRequest, CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<SendResponse>("send", cts, paymentRequest);
        }

        public async Task<SendResponse> Send(string paymentRequest, long amountMsat,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<SendResponse>("send", cts, paymentRequest, amountMsat);
        }

        public async Task<SendResponse> Send(long amountMsat, string paymentHash, string nodeId,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<SendResponse>("send", cts, amountMsat, paymentHash, nodeId);
        }

        public async Task<CheckInvoiceResponse> CheckInvoice(string paymentRequest,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<CheckInvoiceResponse>("checkinvoice", cts, paymentRequest);
        }

        public async Task<CheckInvoiceResponse> ParseInvoice(string paymentRequest,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<CheckInvoiceResponse>("parseinvoice", cts, paymentRequest);
        }

        public async Task<bool> CheckPayment(string paymentRequestOrHash,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<bool>("checkpayment", cts, paymentRequestOrHash);
        }

        public async Task<string> Close(string channelId, CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<string>("close", cts, channelId);
        }

        public async Task<string> Close(string channelId, string scriptPubKey,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<string>("close", cts, channelId, scriptPubKey);
        }

        private async Task<T> SendCommandAsync<T>(string method, CancellationToken cts, params object[] parameters)
        {
            var jsonSerializer = new JsonSerializerSettings
                {ContractResolver = new CamelCasePropertyNamesContractResolver()};

            var body = new JsonRpcCommand(method, parameters);

            var request = new HttpRequestMessage(HttpMethod.Post, string.Empty);
            request.Content = new StringContent(body.ToString(jsonSerializer), Encoding.UTF8, "application/json");

            var rawResult = await _httpClient.SendAsync(request, cts);
            var rawJson = await rawResult.Content.ReadAsStringAsync();
            var result = JObject.Parse(rawJson).ToObject<JsonRpcResult<T>>(JsonSerializer.Create(jsonSerializer));
            if (result.Error != null && !string.IsNullOrEmpty(result.Error.Message))
            {
                throw new InvalidOperationException(result.Error.Message);
            }

            return result.Result;
        }


        internal class JsonRpcResult<T>
        {
            public class JsonRpcResultError
            {
                [JsonProperty("code")] public int Code { get; set; }
                [JsonProperty("message")] public string Message { get; set; }
            }

            [JsonProperty("result")] public T Result { get; set; }
            [JsonProperty("error")] public JsonRpcResultError Error { get; set; }
            [JsonProperty("id")] public string Id { get; set; }
        }

        internal class JsonRpcCommand
        {
            [JsonProperty("jsonRpc")] public string JsonRpc { get; set; } = "2.0";
            [JsonProperty("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
            [JsonProperty("method")] public string Method { get; set; }

            [JsonProperty("params")] public object[] Parameters { get; set; }

            public JsonRpcCommand()
            {
            }

            public JsonRpcCommand(string method, object[] parameters)
            {
                Method = method;
                Parameters = parameters;
            }

            public string ToString(JsonSerializerSettings jsonSerializer)
            {
                return JObject.FromObject(this, JsonSerializer.Create(jsonSerializer)).ToString();
            }
        }
    }


    public class EclairLightningClient : ILightningClient
    {
        private readonly Uri _address;
        private readonly Network _network;
        private readonly RPCClient _rpcClient;
        private readonly ConcurrentDictionary<string, LightningInvoice> _memoryInvoices = new ConcurrentDictionary<string, LightningInvoice>();
        private EclairClient _eclairClient;

        public EclairLightningClient(Uri address, string password, Network network, RPCClient rpcClient)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _address = address;
            _network = network;
            _rpcClient = rpcClient;
            _eclairClient = new EclairClient(address, password);
        }


        public async Task<LightningInvoice> GetInvoice(string invoiceId,
            CancellationToken cancellation = default(CancellationToken))
        {
         return await  UpdateLocalLightningInvoice(invoiceId, cancellation);
        }

        public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation = default(CancellationToken))
        {
            var result = await _eclairClient.Receive(
                description,
                amount.MilliSatoshi,
                Convert.ToInt32(expiry.TotalSeconds), cancellation);

            var decodedBolt = BOLT11PaymentRequest.Parse(result, _network);

            var invoice = new LightningInvoice()
            {
                BOLT11 = result,
                Amount = decodedBolt.MinimumAmount,
                Id = decodedBolt.PaymentHash.ToString(),
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = decodedBolt.ExpiryDate
            };

            _memoryInvoices.TryAdd(invoice.Id, invoice);
            return invoice;

        }

        public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default(CancellationToken))
        {

            var listener = new EclairWebsocketListener(this);
            throw new Exception();
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default(CancellationToken))
        {
            var info = await _eclairClient.GetInfo(cancellation);

            return new LightningNodeInformation()
            {
                NodeInfo = new NodeInfo(new PubKey(info.NodeId), _address.AbsoluteUri,
                    info.Port == 0 ? 9735 : info.Port),
                BlockHeight = info.BlockHeight
            };
        }

        public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default(CancellationToken))
        {
            try
            {
                var sendResult = await _eclairClient.Send(bolt11, cancellation);
                return new PayResponse(PayResult.Ok);
            }
            catch (Exception)
            {
                return new PayResponse(PayResult.CouldNotFindRoute);
            }
        }

        public async Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest,
            CancellationToken cancellation = default(CancellationToken))
        {
            var result = await _eclairClient.OpenChannel(openChannelRequest.NodeInfo.NodeId.ToString(),
                openChannelRequest.ChannelAmount.Satoshi
                , 0,
                Convert.ToInt64(openChannelRequest.FeeRate.SatoshiPerByte), cancellation);

            return new OpenChannelResponse(OpenChannelResult.Ok);
        }

        public async Task<BitcoinAddress> GetDepositAddress()
        {
            return await _rpcClient.GetNewAddressAsync();
        }

        public async Task ConnectTo(NodeInfo nodeInfo)
        {
            await _eclairClient.ConnectToNode(nodeInfo.NodeId.ToString(), nodeInfo.Host, nodeInfo.Port);
        }
        
        private async Task<LightningInvoice> UpdateLocalLightningInvoice(string paymentHash,CancellationToken cancellation)
        {
            var paid = await _eclairClient.CheckPayment(paymentHash, cancellation);
            if (!_memoryInvoices.ContainsKey(paymentHash)) return new LightningInvoice()
            {
                Id = paymentHash,
                Status = paid? LightningInvoiceStatus.Paid: LightningInvoiceStatus.Unpaid,
                PaidAt = paid? DateTimeOffset.Now : (DateTimeOffset?) null
            };
            var invoice = _memoryInvoices[paymentHash];
            if (invoice.ExpiresAt <= DateTimeOffset.Now && !paid)
            {
                invoice.Status = LightningInvoiceStatus.Expired;
            }
            else if (paid)
            {
                invoice.Status = LightningInvoiceStatus.Paid;
                invoice.PaidAt = DateTimeOffset.Now;
            }
            else
            {
                invoice.Status = LightningInvoiceStatus.Unpaid;
            }

            return invoice;

        }

        public class EclairWebsocketListener : ILightningInvoiceListener
        {
            private readonly EclairLightningClient _eclairLightningClient;
            private readonly string _address;
            private WebSocket _websocketConnection;
            private ConcurrentQueue<string> _receivedInvoiceQueue = new ConcurrentQueue<string>();

            public EclairWebsocketListener(EclairLightningClient eclairLightningClient)
            {
                _eclairLightningClient = eclairLightningClient;
                _address = new Uri(_eclairLightningClient._address, "ws")
                    .AbsoluteUri
                    .Replace("https", "wss")
                    .Replace("http", "ws");

                Connect();
            }

            private void Connect()
            {
                _websocketConnection = new WebSocket(_address);
                _websocketConnection.Connect();
                _websocketConnection.OnMessage += WebsocketConnectionOnOnMessage;
            }

            private void WebsocketConnectionOnOnMessage(object sender, MessageEventArgs e)
            {
                if (e.IsText)
                {
                    _receivedInvoiceQueue.Enqueue(e.Data);
                }
            }

            public void Dispose()
            {
                _websocketConnection.Close();
            }

            public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
            {
                while (!cancellation.IsCancellationRequested)
                {
                    if (_receivedInvoiceQueue.IsEmpty)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellation);
                        continue;
                    }

                    if (!_receivedInvoiceQueue.TryDequeue(out var paymentHash)) continue;

                    return await _eclairLightningClient.UpdateLocalLightningInvoice(paymentHash, cancellation);
                    
                }

                return null;
            }

            
        }
    }
}