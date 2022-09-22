using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LNbank.Models;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNbank
{
    public class LNbankClient
    {
        private readonly string _apiToken;
        private readonly Uri _baseUri;
        private readonly HttpClient _httpClient;
        private readonly Network _network;
        private static readonly HttpClient _sharedClient = new HttpClient();

        public LNbankClient(Uri baseUri, string apiToken, Network network, HttpClient httpClient)
        {
            _baseUri = baseUri;
            _apiToken = apiToken;
            _network = network;
            _httpClient = httpClient ?? _sharedClient;
        }

        public async Task<NodeInfoData> GetInfo(CancellationToken cancellation)
        {
            return await Get<NodeInfoData>("info", cancellation);
        }

        public async Task<LightningNodeBalance> GetBalance(CancellationToken cancellation)
        {
            return await Get<LightningNodeBalance>("balance", cancellation);
        }

        public async Task<InvoiceData> GetInvoice(string invoiceId, CancellationToken cancellation)
        {
            return await Get<InvoiceData>($"invoice/{invoiceId}", cancellation);
        }

        public async Task<InvoiceData[]> ListInvoices(ListInvoicesParams param, CancellationToken cancellation)
        {
            var path = new StringBuilder("invoices");
            if (param != null)
            {
                if (param is { PendingOnly: true }) path.Append("pending_only=true&");
                if (param.OffsetIndex.HasValue) path.Append("offset_index=").Append(param.OffsetIndex.Value);
            }
            
            return await Get<InvoiceData[]>(path.ToString(), cancellation);
        }

        public async Task<PaymentData> GetPayment(string paymentHash, CancellationToken cancellation)
        {
            return await Get<PaymentData>($"payment/{paymentHash}", cancellation);
        }

        public async Task CancelInvoice(string invoiceId, CancellationToken cancellation)
        {
            await Send<EmptyRequestModel, EmptyRequestModel>(HttpMethod.Delete, $"invoice/{invoiceId}", new EmptyRequestModel(), cancellation);
        }

        public async Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default)
        {
            var address = await Post<EmptyRequestModel, string>("deposit-address", null, cancellation);

            return BitcoinAddress.Create(address, _network);
        }

        public async Task<ChannelData[]> ListChannels(CancellationToken cancellation)
        {
            return await Get<ChannelData[]>("channels", cancellation);
        }

        public async Task<InvoiceData> CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation)
        {
            var payload = new CreateInvoiceRequest
            {
                Amount = req.Amount,
                Description = req.Description,
                DescriptionHash = req.DescriptionHash,
                Expiry = req.Expiry,
                PrivateRouteHints = req.PrivateRouteHints
            };
            return await Post<CreateInvoiceRequest, InvoiceData>("invoice", payload, cancellation);
        }

        public async Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation)
        {
            var payload = new PayInvoiceRequest
            {
                PaymentRequest = bolt11,
                MaxFeePercent = payParams?.MaxFeePercent,
                MaxFeeFlat = payParams?.MaxFeeFlat?.Satoshi
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
                RequestUri = new Uri($"{_baseUri}plugins/lnbank/api/lightning/{path}"),
                Method = method,
                Content = content
            };
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
            req.Headers.Add("User-Agent", "BTCPayServer.Lightning.LNbankClient");

            var res = await _httpClient.SendAsync(req, cancellation);
            var str = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                if (res.StatusCode.Equals(422))
                {
                    var validationErrors = JsonConvert.DeserializeObject<GreenfieldValidationErrorData[]>(str);
                    var message = string.Join(", ", validationErrors.Select(ve => $"{ve.Path}: {ve.Message}"));
                    var err = new GreenfieldApiErrorData("validation-failed", message);
                    throw new LNbankApiException(err);
                } else {
                    var err = JsonConvert.DeserializeObject<GreenfieldApiErrorData>(str);
                    throw new LNbankApiException(err);
                }
            }

            if (typeof(TResponse) == typeof(EmptyRequestModel))
            {
                return (TResponse)(object)new EmptyRequestModel();
            }
            var data = JsonConvert.DeserializeObject<TResponse>(str);

            return data;
        }

        private class EmptyRequestModel
        {
        }
        
        internal class LNbankApiException : Exception
        {
            private readonly GreenfieldApiErrorData _error;

            public override string Message => _error?.Message;
            public string ErrorCode => _error?.Code;
            
            public LNbankApiException(GreenfieldApiErrorData error)
            {
                _error = error;
            }
        }
    }
}
