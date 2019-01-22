using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Eclair.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Lightning.Eclair
{
    public class EclairClient
    {
        private HttpClient _httpClient;


        public EclairClient(Uri address, string password)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));

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
}