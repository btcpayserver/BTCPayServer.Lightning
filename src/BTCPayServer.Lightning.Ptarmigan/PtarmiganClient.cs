using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Ptarmigan.Models;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Lightning.Ptarmigan
{
    public class PtarmiganClient
    {

        private readonly Uri _address;
        private readonly HttpClient _httpClient;
        private readonly string _apiToken;
        private static readonly HttpClient SharedClient = new HttpClient();

        public PtarmiganClient(Uri address, string apiToken, HttpClient httpClient = null)
        {
            _address = address;
            _apiToken = apiToken;
            _httpClient = httpClient ?? SharedClient;
        }

        public async Task<GetInfoResponse> GetInfo(CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<NoRequestModel, GetInfoResponse>("getinfo", NoRequestModel.Instance, cts);
        }

        public async Task<InvoiceResponse> CreateInvoice(long amountMsat, string description, int invoiceExpiry, CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<CreateInvoiceRequest, InvoiceResponse>("createinvoice",
               new CreateInvoiceRequest()
               {
                   AmountMsat = amountMsat,
                   Description = description,
                   InvoiceExpiry = invoiceExpiry
               }, cts);
        }

        public async Task<SendPaymentResponse> SendPayment(string bolt11, int amounaddAmountMsat = 0,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<SendPaymentRequest, SendPaymentResponse>("sendpayment",
                new SendPaymentRequest()
                {
                    Bolt11 = bolt11,
                    AddAmountMsat = amounaddAmountMsat
                }, cts);
        }

        public async Task<ConnectResponse> Connect(PubKey nodeId, string peerAddr, int? peerPort = null,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<ConnectRequest, ConnectResponse>("connect",
                new ConnectRequest()
                {
                    PeerNodeId = nodeId.ToString(),
                    PeerAddr = peerAddr,
                    PeerPort = peerPort,
                }, cts);
        }

        public async Task<OpenResponse> OpenChannel(PubKey nodeId, long fundingSat = 0, int pushMsat = 0, long feeratePerKw = 0,
            CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<OpenRequest, OpenResponse>("openchannel",
                new OpenRequest()
                {
                    PeerNodeId = nodeId.ToString(),
                    FundingSat = fundingSat,
                    PushMsat = pushMsat,
                    FeeratePerKw = feeratePerKw,
                }, cts);
        }

        public async Task<ListPaymentResponse> ListPayment(int listPaymentId, CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<ListPaymentRequest, ListPaymentResponse>("listpayment",
                new ListPaymentRequest()
                {
                    listPaymentId = listPaymentId
                }, cts);
        }

        public async Task<ListInvoiceResultResponse> GetLatestInvoice(CancellationToken cts = default(CancellationToken))
        {
            var listInvoiceResponseList = await SendCommandAsync<NoRequestModel, ListInvoiceResponse>("listinvoices", NoRequestModel.Instance, cts);
            string dateFormat = "yyyy-MM-ddTHH:mm:ssZ";
            var lastCreationTime = DateTime.MinValue;
            ListInvoiceResultResponse result = null;

            foreach (ListInvoiceResultResponse listInvoiceResultResponse in listInvoiceResponseList.Result)
            {
                var creationTime = DateTime.ParseExact(listInvoiceResultResponse.CreationTime, dateFormat, null);
                if (lastCreationTime < creationTime)
                {
                    lastCreationTime = creationTime;
                    result = listInvoiceResultResponse;
                }
            }
            return result;
        }

        public async Task<ListInvoiceResponse> ListInvoice(string paymentHash, CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<ListInvoiceRequest, ListInvoiceResponse>("listinvoices",
                new ListInvoiceRequest()
                {
                    PaymentHash = paymentHash
                }, cts);
        }

        public async Task<ListInvoiceResponse> ListAllInvoice(CancellationToken cts = default(CancellationToken))
        {
            return await SendCommandAsync<ListInvoiceRequest, ListInvoiceResponse>("listinvoices",
                new ListInvoiceRequest()
                {
                }, cts);
        }

        private async Task<TResponse> SendCommandAsync<TRequest, TResponse>(string method, TRequest data, CancellationToken cts)
        {
            var jsonSerializer = new JsonSerializerSettings
            { ContractResolver = new CamelCasePropertyNamesContractResolver() };

            HttpContent content = null;
            if (data != null && !(data is NoRequestModel))
            {
                var json = JsonConvert.SerializeObject(data);
                var stringContent = new StringContent(json, Encoding.UTF8, "application/json");
                content = stringContent;
            }

            var httpRequest = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(_address, method),
                Content = content
            };
            httpRequest.Headers.Accept.Clear();
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Headers.Add("Authorization", "Bearer " + _apiToken);

            var rawResult = await _httpClient.SendAsync(httpRequest, cts);
            var rawJson = await rawResult.Content.ReadAsStringAsync();
            if (!rawResult.IsSuccessStatusCode)
            {
                throw new PtarmiganApiException()
                {
                    Error = JsonConvert.DeserializeObject<PtarmiganApiError>(rawJson, jsonSerializer)
                };
            }

            try
            {
                return JsonConvert.DeserializeObject<TResponse>(rawJson, jsonSerializer);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(rawJson);
                throw;
            }

        }

        internal class NoRequestModel
        {
            public static NoRequestModel Instance = new NoRequestModel();
        }

        internal class PtarmiganApiException : Exception
        {
            public PtarmiganApiError Error { get; set; }

            public override string Message => Error?.Error;
        }

        internal class PtarmiganApiError
        {
            public string Error { get; set; }
        }

    }

}