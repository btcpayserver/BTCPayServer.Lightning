using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LNDhub.Models;
using NBitcoin;
using NBitcoin.Crypto;
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
        private readonly Network _network;
        private static readonly HttpClient _sharedClient = new ();
        private static readonly ConcurrentDictionary<string, AuthResponse> _cache = new();
        public readonly string CacheKey;
        private readonly AsyncDuplicateLock _locker = new();

        public LndHubClient(Uri baseUri, string login, string password, Network network, HttpClient httpClient)
        {
            _login = login;
            _password = password;
            _network = network;
            _baseUri = baseUri;
            _httpClient = httpClient ?? _sharedClient;

            CacheKey = ConvertHelper.ToHexString(Hashes.SHA256(Encoding.UTF8.GetBytes(_baseUri+ _login + _password)));
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
                Amount = req.Amount,
                Memo = req.Description ?? string.Empty,
                DescriptionHash = req.DescriptionHash
            };

            return await Post<CreateInvoiceRequest, InvoiceData>("addinvoice", payload, cancellation);
        }

        public async Task<PaymentResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation)
        {
            var payload = new PayInvoiceRequest
            {
                PaymentRequest = bolt11,
            };

            if (payParams?.Amount != null)
                payload.Amount = payParams.Amount;

            return await Post<PayInvoiceRequest, PaymentResponse>("payinvoice", payload, cancellation);
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

            if (!path.StartsWith("auth") && path != "create")
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessToken(cancellation));
            }

            var res = await _httpClient.SendAsync(req, cancellation);
            var str = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode || str.StartsWith("{\"error\":true,"))
            {
                var exception = new LndHubApiException(str);
                if (!exception.AuthenticationFailed || isAuthRetry) throw exception;

                await ClearAccessToken();
                return await Send<TRequest, TResponse>(method, path, payload, true, cancellation);
            }

            if (typeof(TResponse) == typeof(EmptyRequestModel))
            {
                return (TResponse)(object)new EmptyRequestModel();
            }
            var data = JsonConvert.DeserializeObject<TResponse>(str);
            
            // Handle edge case: LNDhub returns only the PaymentData in case of self-payment
            if (path == "payinvoice" && data is PaymentResponse { Decoded: null })
            {
                var resp = new PaymentResponse
                {
                    PaymentError = "",
                    Decoded = JsonConvert.DeserializeObject<PaymentData>(str)
                };
                return (TResponse)Convert.ChangeType(resp, typeof(TResponse));
            }

            return data;
        }

        public async Task<ILightningInvoiceListener> CreateInvoiceSession(CancellationToken cancellation = default)
        {
            var at = await GetAccessToken(cancellation);
            if (at is null) return null;
            var session = new LndHubInvoiceListener(this, cancellation);
            return session;
        }

        private async Task ClearAccessToken()
        {
            using var release = await _locker.LockAsync(CacheKey);
            _cache.TryRemove(CacheKey, out _);
        }

        public async Task<string> GetAccessToken(CancellationToken cancellation = default)
        {
            using var release = await _locker.LockAsync(CacheKey);
            AuthResponse response;
            if (_cache.TryGetValue(CacheKey, out var cached))
            {
                if (cached.Expiry <= DateTimeOffset.UtcNow)
                {
                    _cache.TryRemove(CacheKey, out _);
                }
                else if ((cached.Expiry - DateTimeOffset.UtcNow) > TimeSpan.FromMinutes(5))
                {
                    return cached.AccessToken;
                }

                response = await Post<AuthRequest, AuthResponse>("auth?type=refresh_token",
                    new AuthRequest {RefreshToken = cached.RefreshToken}, cancellation);
                
            }
            else
            {
                response = await Post<AuthRequest, AuthResponse>("auth?type=auth",
                    new AuthRequest {Login = _login, Password = _password}, cancellation);
            }

            if (response.Expiry is null)
            {
                try
                {
                    response.Expiry = DateTimeOffset.FromUnixTimeSeconds(
                        long.Parse(ParseClaimsFromJwt(response.AccessToken).First(claim => claim.Type == "exp").Value));
                }
                catch (Exception)
                {
                    //it's ok if we dont parse it, once auth fails we try again
                }
            }

            _cache.AddOrReplace(CacheKey, response);
            return response.AccessToken;
        }
        private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
        {
            var payload = jwt.Split('.')[1];
            var jsonBytes = ParseBase64WithoutPadding(payload);
            var keyValuePairs = JObject.Parse(Encoding.UTF8.GetString(jsonBytes)).ToObject<Dictionary<string, object>>();
            return keyValuePairs.Select(kvp => new Claim(kvp.Key, kvp.Value.ToString()));
        }

        private static byte[] ParseBase64WithoutPadding(string base64)
        {
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }

        private static string WithTrailingSlash(string str) =>
            str.EndsWith("/", StringComparison.InvariantCulture) ? str : str + "/";

        private class EmptyRequestModel
        {
        }

        public class LndHubApiException : Exception
        {
            private ErrorResponse Error { get; set; }

            public override string Message => Error?.Message;
            public int ErrorCode => Error.Code;
            public bool AuthenticationFailed => ErrorCode == 1;
            public LndHubApiException(string json)
            {
                Error = JsonConvert.DeserializeObject<ErrorResponse>(json);
            }
        }
    }
}
