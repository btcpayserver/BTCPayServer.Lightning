using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Phoenixd.Models;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.Phoenixd
{
    public class PhoenixdClient
    {
        private readonly Uri _address;
        private readonly string _username;
        private readonly string _password;
        private readonly HttpClient _httpClient;
        private static readonly HttpClient SharedClient = new();

        public Network Network { get; }

        public PhoenixdClient(Uri address, string password, Network network, HttpClient httpClient = null) : this(address, null, password, network, httpClient) { }
        public PhoenixdClient(Uri address, string username, string password, Network network, HttpClient httpClient = null)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _address = address;
            _username = username;
            _password = password;
            Network = network;
            _httpClient = httpClient ?? SharedClient;
        }

        public async Task<GetInfoResponse> GetInfo(CancellationToken cts = default)
        {
            return await SendCommandAsync<NoRequestModel, GetInfoResponse>("getinfo", NoRequestModel.Instance, cts, true);
        }

        public async Task<GetBalanceResponse> GetBalance(CancellationToken cts = default)
        {
            return await SendCommandAsync<NoRequestModel, GetBalanceResponse>("getbalance", NoRequestModel.Instance, cts, true);
        }

        public async Task<CreateInvoiceResponse> CreateInvoice(string description, long? amountSat = null,
            int? expirySeconds = null, BitcoinAddress fallbackAddress = null,
            CancellationToken cts = default)
        {
            return await SendCommandAsync<CreateInvoiceRequest, CreateInvoiceResponse>("createinvoice",
                new CreateInvoiceRequest
                {
                    Description = description,
                    ExpirySeconds = expirySeconds,
                    AmountSat = amountSat == 0 ? null : amountSat
                }, cts);
        }

        public async Task<PayInvoiceResponse> PayInvoice(string bolt11, long? amountSat = null, CancellationToken cts = default)
        {
            return await SendCommandAsync<PayInvoiceRequest, PayInvoiceResponse>("payinvoice",
                new PayInvoiceRequest
                {
                    Invoice = bolt11,
                    AmountSat = amountSat
                }, cts);
        }

        public async Task<GetIncomingPaymentResponse> GetIncomingPayment(string paymentHash, string invoice = null,
            CancellationToken cts = default)
        {
            return await SendCommandAsync<NoRequestModel, GetIncomingPaymentResponse>($"payments/incoming/{paymentHash}", NoRequestModel.Instance, cts, true);
        }

        public async Task<GetOutgoingPaymentResponse> GetOutgoingPayment(string paymentHash, string invoice = null,
            CancellationToken cts = default)
        {
            return await SendCommandAsync<NoRequestModel, GetOutgoingPaymentResponse>($"payments/outgoingbyhash/{paymentHash}", NoRequestModel.Instance, cts, true);
        }

        public async Task<string> SendPayment(string address, long amountSat, long feerateSat,
            CancellationToken cts = default)
        {
            return await SendCommandAsync<SendPaymentRequest, string>($"sendtoaddress",
                new SendPaymentRequest
                {
                    AmountSat = amountSat,
                    Address = address,
                    FeerateSatByte = feerateSat
                }, cts);
        }

        JsonSerializer _Serializer;
        JsonSerializerSettings _SerializerSettings;
        JsonSerializerSettings SerializerSettings
        {
            get
            {
                if (_SerializerSettings == null)
                {
                    var jsonSerializer = new JsonSerializerSettings();
                    NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(jsonSerializer, Network);
                    _SerializerSettings = jsonSerializer;
                }
                return _SerializerSettings;
            }
        }
        JsonSerializer Serializer
        {
            get
            {
                if (_Serializer == null)
                {
                    _Serializer = JsonSerializer.Create(SerializerSettings);
                }
                return _Serializer;
            }
        }

        private async Task<TResponse> SendCommandAsync<TRequest, TResponse>(string method, TRequest data, CancellationToken cts, bool httpGet = false)
        {
            HttpContent content = null;
            if (data != null && !(data is NoRequestModel))
            {
                var jobj = JObject.FromObject(data, Serializer);
                Dictionary<string, string> x = new Dictionary<string, string>();
                foreach (var item in jobj)
                {
                    if (item.Value == null || (item.Value.Type == JTokenType.Null))
                    {
                        continue;
                    }
                    x.Add(item.Key, item.Value.ToString());
                }
                content = new FormUrlEncodedContent(x.Select(pair => pair));
            }

            int retry = 0;
retry:
            var httpRequest = new HttpRequestMessage
            {
                Method = httpGet ? HttpMethod.Get : HttpMethod.Post,
                RequestUri = new Uri(_address, method),
                Content = content
            };
            httpRequest.Headers.Accept.Clear();
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.Default.GetBytes($"{_username ?? string.Empty}:{_password}")));
            try
            {
                using var rawResult = await _httpClient.SendAsync(httpRequest, cts);
                var rawJson = await rawResult.Content.ReadAsStringAsync();
                if (!rawResult.IsSuccessStatusCode)
                {
                    PhoenixdApiError apiError = null;

                    try
                    {
                        apiError = JsonConvert.DeserializeObject<PhoenixdApiError>(rawJson, SerializerSettings);
                    }
                    catch
                    {
                        apiError = new PhoenixdApiError { Error = rawJson };
                    }

                    throw new PhoenixdApiException { Error = apiError };
                }

                try
                {
                    return JsonConvert.DeserializeObject<TResponse>(rawJson, SerializerSettings);
                }
                catch
                {
                    if (typeof(TResponse) == typeof(string))
                        return (TResponse)(object)rawJson;
                    throw new PhoenixdApiException { Error = new PhoenixdApiError { Error = rawJson } };
                }
            }
            catch (HttpRequestException e) when (e.InnerException is IOException && retry < 10)
            {
                retry++;
                await Task.Delay(100 * retry, cts);
                goto retry;
            }
        }


        internal class NoRequestModel
        {
            public static NoRequestModel Instance = new NoRequestModel();
        }

        internal class PhoenixdApiException : Exception
        {
            public PhoenixdApiError Error { get; set; }

            public override string Message => Error?.Error;
        }

        internal class PhoenixdApiError
        {
            public string Error { get; set; }
        }
    }
}
