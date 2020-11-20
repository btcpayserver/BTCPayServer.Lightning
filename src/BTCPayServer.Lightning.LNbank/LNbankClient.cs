using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Text;
using BTCPayServer.Lightning.LNbank.Models;
using Newtonsoft.Json;
using NBitcoin;
using NBitcoin.JsonConverters;

namespace BTCPayServer.Lightning.LNbank
{
    public class LNbankClient
    {
        private readonly string _walletId;
        private readonly HttpClient _httpClient;
        private readonly JsonSerializer _serializer;
        private readonly Network _network;

        public LNbankClient(Uri baseUri, string apiToken, string walletId, Network network, HttpClient httpClient)
        {
            _walletId = walletId;
            _httpClient = httpClient;
            _network = network;

            // HTTP
            _httpClient.BaseAddress = new Uri($"{baseUri}api/lightning/");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "BTCPayServer.Lightning.LNbankLightningClient");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", apiToken);

            // JSON
            var serializerSettings = new JsonSerializerSettings();
            Serializer.RegisterFrontConverters(serializerSettings, network);
            _serializer = JsonSerializer.Create(serializerSettings);
        }

        public async Task<NodeInfoData> GetInfo(CancellationToken cancellation)
        {
            return await Get<NodeInfoData>("info", cancellation);
        }

        public async Task<InvoiceData> GetInvoice(string invoiceId, CancellationToken cancellation)
        {
            return await Get<InvoiceData>($"invoice/{invoiceId}", cancellation);
        }

        public async Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default)
        {
            var addr = await Post<EmptyRequestModel, string>("deposit-address", null, cancellation);

            return BitcoinAddress.Create(addr, _network);
        }

        public async Task<ChannelData[]> ListChannels(CancellationToken cancellation)
        {
            return await Get<ChannelData[]>("channels", cancellation);
        }

        public async Task<InvoiceData> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation)
        {
            var payload = new CreateInvoiceRequest
            {
                WalletId = _walletId,
                Amount = amount,
                Description = description,
                Expiry = expiry
            };
            return await Post<CreateInvoiceRequest, InvoiceData>("invoice", payload, cancellation);
        }

        public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation)
        {
            var payload = new PayInvoiceRequest
            {
                WalletId = _walletId,
                PaymentRequest = bolt11
            };
            return await Post<PayInvoiceRequest, PayResponse>("pay", payload, cancellation);
        }

        public async Task<OpenChannelResponse> OpenChannel(NodeInfo nodeUri, Money amount, FeeRate feeRate, CancellationToken cancellation)
        {
            var payload = new CreateChannelRequest
            {
                NodeURI = nodeUri.ToString(),
                ChannelAmount = amount,
                FeeRate = feeRate
            };
            return await Post<CreateChannelRequest, OpenChannelResponse>("channels", payload, cancellation);
        }

        public async Task ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = default)
        {
            var payload = new ConnectNodeRequest
            {
                NodeURI = nodeInfo.ToString()
            };
            await Post<ConnectNodeRequest, string>("connect", payload, cancellation);
        }

        private async Task<TResponse> Get<TResponse>(string path, CancellationToken cancellation)
        {
            return await Send<EmptyRequestModel, TResponse>(HttpMethod.Get, path, null, cancellation);
        }

        private async Task<TResponse> Post<TRequest, TResponse>(string path, TRequest payload, CancellationToken cancellation)
        {
            return await Send<TRequest, TResponse>(HttpMethod.Post, path, payload, cancellation);
        }

        private async Task<TResponse> Send<TRequest, TResponse>(HttpMethod method, string path, TRequest payload, CancellationToken cancellation)
        {
            HttpContent content = null;
            if (payload != null)
            {
                var payloadJson = JsonConvert.SerializeObject(payload);
                content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            }
            var req = new HttpRequestMessage
            {
                RequestUri = new Uri(_httpClient.BaseAddress + path),
                Method = method,
                Content = content
            };
            var res = await _httpClient.SendAsync(req, cancellation);
            var str = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                throw new LNbankApiException(str);
            }

            var data = JsonConvert.DeserializeObject<TResponse>(str);

            return data;
        }

        internal class EmptyRequestModel
        {
        }

        internal class LNbankApiException : Exception
        {
            public ErrorData Error { get; set; }

            public override string Message => Error?.Detail;
            public string ErrorCode => Error?.Code;
            public LNbankApiException(string json)
            {
                Error = JsonConvert.DeserializeObject<ErrorData>(json);
            }
        }
    }
}
