using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LNDhub.Models;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/* Docs:
 * https://ln.getalby.com/swagger/index.html#/Info/get_getinfo
 * https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md
 * https://github.com/BlueWallet/BlueWallet/blob/master/class/wallets/lightning-custodian-wallet.js
 */
namespace BTCPayServer.Lightning.LndHub
{
    public class LndHubClient
    {
        private readonly Uri _baseUri;
        private readonly HttpClient _httpClient;
        private readonly string _login;
        private readonly string _password;
        private readonly JsonSerializer _serializer;
        private readonly Network _network;
        private static readonly HttpClient _sharedClient = new HttpClient();

        private string AccessToken { get; set; }
        private string RefreshToken { get; set; }

        public LndHubClient(Uri baseUri, string login, string password, Network network, HttpClient httpClient)
        {
            _login = login;
            _password = password;
            _network = network;
            _baseUri = baseUri;
            _httpClient = httpClient ?? _sharedClient;

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

        public async Task<BalanceData> GetBalance(CancellationToken cancellation)
        {
            return await Get<BalanceData>("balance", cancellation);
        }

        public async Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default)
        {
            var list = await Get<JObject[]>("getbtc", cancellation);
            var item = list.First();
            return item.ContainsKey("address")
                ? BitcoinAddress.Create(item.Property("address").Value.Value<string>(), _network)
                : null;
        }

        public async Task<InvoiceData[]> GetInvoices(CancellationToken cancellation)
        {
            return await Get<InvoiceData[]>("getuserinvoices", cancellation);
        }

        public async Task<TransactionData[]> GetTransactions(CancellationToken cancellation)
        {
            return await Get<TransactionData[]>("gettxs", cancellation);
        }

        public async Task<InvoiceData> CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation)
        {
            var payload = new CreateInvoiceRequest
            {
                Amount = (long)req.Amount.ToUnit(LightMoneyUnit.Satoshi),
                Memo = req.Description ?? string.Empty,
                DescriptionHash = req.DescriptionHash
            };

            return await Post<CreateInvoiceRequest, InvoiceData>("addinvoice", payload, cancellation);
        }

        public async Task<PaymentData> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation)
        {
            var payload = new PayInvoiceRequest
            {
                PaymentRequest = bolt11,
            };

            if (payParams?.Amount != null)
                payload.Amount = (long)payParams.Amount.ToUnit(LightMoneyUnit.Satoshi);

            return await Post<PayInvoiceRequest, PaymentData>("payinvoice", payload, cancellation);
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
            return await Send<TRequest, TResponse>(method, path, payload, false, cancellation);;
        }
        
        private async Task<TResponse> Send<TRequest, TResponse>(HttpMethod method, string path, TRequest payload, bool isAuthRetry, CancellationToken cancellation)
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
                if (string.IsNullOrEmpty(AccessToken))
                {
                    await Authorize(cancellation);
                }
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            }

            var res = await _httpClient.SendAsync(req, cancellation);
            var str = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode || str.StartsWith("{\"error\":true,"))
            {
                var exception = new LndHubApiException(str);
                if (!exception.AuthenticationFailed || isAuthRetry) throw exception;
                
                // unset auth tokens and retry
                AccessToken = RefreshToken = null;
                return await Send<TRequest, TResponse>(method, path, payload, true, cancellation);
            }

            if (typeof(TResponse) == typeof(EmptyRequestModel))
            {
                return (TResponse)(object)new EmptyRequestModel();
            }
            var data = JsonConvert.DeserializeObject<TResponse>(str);

            return data;
        }

        public async Task<ILightningInvoiceListener> CreateInvoiceSession(CancellationToken cancellation = default)
        {
            if (await Authorize(cancellation))
            {
                var streamUrl = WithTrailingSlash(_baseUri.ToString()) + "invoices/stream";
                var session = new LndHubInvoiceListener(this);
                await session.StartListening(streamUrl, AccessToken, cancellation);
                return session;
            }

            return null;
        }

        private async Task<bool> Authorize(CancellationToken cancellation = default)
        {
            var payload = new AuthRequest { Login = _login, Password = _password };
            var response = await Post<AuthRequest, AuthResponse>("auth", payload, cancellation);

            AccessToken = response.AccessToken;
            RefreshToken = response.RefreshToken;

            return !string.IsNullOrEmpty(AccessToken);
        }

        private static string WithTrailingSlash(string str) =>
            str.EndsWith("/", StringComparison.InvariantCulture) ? str :str + "/";

        private class EmptyRequestModel
        {
        }

        public class LndHubApiException : Exception
        {
            // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#general-error-response
            private enum ErrorCodes : int
            {
                BAD_AUTH = 1,
                NOT_ENOUGH_BALANCE = 2,
                BAD_PARTNER = 3,
                INVALID_INVOICE = 4,
                ROUTE_NOT_FOUND = 5,
                GENERAL_SERVER_ERROR = 7,
                LND_FAILURE = 7
            }

            private ErrorData Error { get; set; }

            public override string Message => Error?.Message;
            public int ErrorCode => Error.Code;
            public bool AuthenticationFailed => Error.Code == (int)ErrorCodes.BAD_AUTH;
            public LndHubApiException(string json)
            {
                Error = JsonConvert.DeserializeObject<ErrorData>(json);
            }
        }
    }
}
