using System;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Lightning.LND
{
    public partial class LndSwaggerClient
    {
        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>CancelInvoice cancels a currently open invoice. If the invoice is already
        /// <br/>canceled, this call will succeed. If the invoice is already settled, it will
        /// <br/>fail.</summary>
        /// <returns>A successful response.</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public async Task<InvoicesrpcCancelInvoiceResp> CancelInvoiceAsync(InvoicesrpcCancelInvoiceMsg body,
            CancellationToken cancellationToken)
        {
            if (body == null)
                throw new System.ArgumentNullException("body");

            var urlBuilder_ = new System.Text.StringBuilder();
            urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/v2/invoices/cancel");

            var client_ = _httpClient;
            var disposeClient_ = false;
            try
            {
                using (var request_ = new System.Net.Http.HttpRequestMessage())
                {
                    var content_ =
                        new System.Net.Http.StringContent(
                            Newtonsoft.Json.JsonConvert.SerializeObject(body, _settings.Value));
                    content_.Headers.ContentType =
                        System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");
                    request_.Content = content_;
                    request_.Method = new System.Net.Http.HttpMethod("POST");
                    request_.Headers.Accept.Add(
                        System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("application/json"));

                    PrepareRequest(client_, request_, urlBuilder_);

                    var url_ = urlBuilder_.ToString();
                    request_.RequestUri = new System.Uri(url_, System.UriKind.RelativeOrAbsolute);

                    PrepareRequest(client_, request_, url_);

                    var response_ = await client_.SendAsync(request_,
                            System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                    var disposeResponse_ = true;
                    try
                    {
                        var headers_ =
                            System.Linq.Enumerable.ToDictionary(response_.Headers, h_ => h_.Key, h_ => h_.Value);
                        if (response_.Content != null && response_.Content.Headers != null)
                        {
                            foreach (var item_ in response_.Content.Headers)
                                headers_[item_.Key] = item_.Value;
                        }

                        ProcessResponse(client_, response_);


                        var status_ = (int)response_.StatusCode;
                        if (status_ == 200)
                        {
                            var responseData_ = await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                            var result_ = default(InvoicesrpcCancelInvoiceResp);
                            try
                            {
                                result_ = Newtonsoft.Json.JsonConvert.DeserializeObject<InvoicesrpcCancelInvoiceResp>(
                                    responseData_, _settings.Value);
                                return result_;
                            }
                            catch (System.Exception exception)
                            {
                                throw new SwaggerException("Could not deserialize the response body.",
                                    status_.ToString(), responseData_, headers_, exception);
                            }
                        }
                        else
                        {
                            var responseData_ = await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                            throw new SwaggerException(
                                "The HTTP status code of the response was not expected (" +
                                (int)response_.StatusCode + ").", status_.ToString(), responseData_, headers_,
                                null);
                        }
                    }
                    finally
                    {
                        if (disposeResponse_)
                            response_.Dispose();
                    }
                }
            }
            finally
            {
                if (disposeClient_)
                    client_.Dispose();
            }
        }
    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "10.5.2.0 (Newtonsoft.Json v12.0.0.0)")]
    public partial class InvoicesrpcCancelInvoiceMsg
    {
        /// <summary>Hash corresponding to the (hold) invoice to cancel.</summary>
        [Newtonsoft.Json.JsonProperty("payment_hash", Required = Newtonsoft.Json.Required.Default,
            NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public byte[] Payment_hash { get; set; }
    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "10.5.2.0 (Newtonsoft.Json v12.0.0.0)")]
    public partial class InvoicesrpcCancelInvoiceResp
    {
    }
}