using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LNDhub.Models;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LndHub
{
    public class LndHubClient
    {
        private readonly Uri _baseUri;
        private readonly string _login;
        private readonly string _password;
        private readonly HttpClient _httpClient;
        private readonly JsonSerializer _serializer;
        private readonly Network _network;
        private static readonly HttpClient SharedClient = new HttpClient();

        public LndHubClient(Uri baseUri, string login, string password, Network network, HttpClient httpClient)
        {
            _baseUri = baseUri;
            _login = login;
            _password = password;
            _network = network;
            _httpClient = httpClient ?? SharedClient;

            // JSON
            var serializerSettings = new JsonSerializerSettings();
            Serializer.RegisterFrontConverters(serializerSettings, network);
            _serializer = JsonSerializer.Create(serializerSettings);
        }

        public async Task<CreateAccountResponse> CreateAccount(CancellationToken cancellation)
        {
            var payload = new CreateAccountRequest { AccountType = "test" };
            return await Post<CreateAccountRequest, CreateAccountResponse>("create", payload, cancellation);
        }

        public async Task<NodeInfoData> GetInfo(CancellationToken cancellation)
        {
            return await Get<NodeInfoData>("getinfo", cancellation);
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
                RequestUri = new Uri($"{_baseUri}{path}"),
                Method = method,
                Content = content
            };
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Add("User-Agent", "BTCPayServer.Lightning.LndHubClient");

            if (path != "auth" && path != "create")
            {
                var accessToken = await GetAccessToken(cancellation);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            var res = await _httpClient.SendAsync(req, cancellation);
            var str = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                throw new LndHubApiException(str);
            }

            if (typeof(TResponse) == typeof(EmptyRequestModel))
            {
                return (TResponse)(object)new EmptyRequestModel();
            }
            var data = JsonConvert.DeserializeObject<TResponse>(str);

            return data;
        }

        private async Task<string> GetAccessToken(CancellationToken cancellation = default)
        {
            var payload = new AuthRequest
            {
                Login = _login,
                Password = _password
            };
            var response = await Post<AuthRequest, AuthResponse>("auth", payload, cancellation);

            return response.AccessToken;
        }

        internal class EmptyRequestModel
        {
        }

        internal class LndHubApiException : Exception
        {
            private ErrorData Error { get; set; }

            public override string Message => Error?.Message;
            public int ErrorCode => Error.Code;
            public LndHubApiException(string json)
            {
                Error = JsonConvert.DeserializeObject<ErrorData>(json);
            }
        }
    }
}
