using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.LND
{
    public class LndException : Exception
    {
        public LndException(string message) : base(message)
        {

        }
        public LndException(LndError error) : base(error.Message)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            _Error = error;
        }


        private readonly LndError _Error;
        public LndError Error
        {
            get
            {
                return _Error;
            }
        }
    }
    // {"grpc_code":2,"http_code":500,"message":"rpc error: code = Unknown desc = expected 1 macaroon, got 0","http_status":"Internal Server Error"}
    public class LndError
    {
        [JsonProperty("grpc_code")]
        public int GRPCCode { get; set; }
        [JsonProperty("http_code")]
        public int HttpCode { get; set; }
        [JsonProperty("message")]
        public string Message { get; set; }
        [JsonProperty("http_status")]
        public string HttpStatus { get; set; }
    }
    public partial class LndSwaggerClient
    {
        public LndSwaggerClient(LndRestSettings settings) : this(settings, null)
        {

        }
        public LndSwaggerClient(LndRestSettings settings, HttpClient httpClient)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            _LndSettings = settings;
            _Authentication = settings.CreateLndAuthentication();
            BaseUrl = settings.Uri.AbsoluteUri.TrimEnd('/');
            _httpClient = CreateHttpClient(settings, httpClient);
            _settings = new System.Lazy<Newtonsoft.Json.JsonSerializerSettings>(() =>
            {
                var json = new Newtonsoft.Json.JsonSerializerSettings();
                UpdateJsonSerializerSettings(json);
                return json;
            });
        }
        LndRestSettings _LndSettings;
        internal LndAuthentication _Authentication;

        partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
        {
            _Authentication.AddAuthentication(request);
        }

        internal static HttpClient CreateHttpClient(LndRestSettings settings, HttpClient httpClient)
        {
            if (httpClient != null)
            {
                // If we allow insecure and want http, we don't need specific http handlers
                if (settings.AllowInsecure)
                {
                    if (settings.Uri.Scheme == "http")
                        return httpClient;
                }
                // If we do not allow insecure and want https and do not pin certificates, we don't need specific http handlers
                else if (settings.CertificateThumbprint == null
                         && settings.Uri.Scheme == "https")
                {
                    return httpClient;
                }
            }

            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12
            };

            var expectedThumbprint = settings.CertificateThumbprint?.ToArray();
            if (expectedThumbprint != null)
            {
                handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
                {
                    var actualCert = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
                    var hash = GetHash(actualCert);
                    return hash.SequenceEqual(expectedThumbprint);
                };
            }

            if (settings.AllowInsecure)
            {
                handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) => true;
            }
            else
            {
                if (settings.Uri.Scheme == "http")
                    throw new InvalidOperationException("AllowInsecure is set to false, but the URI is not using https");
            }
            return new HttpClient(handler);
        }

        private static byte[] GetHash(X509Certificate2 cert)
        {
            using (HashAlgorithm alg = SHA256.Create())
            {
                return alg.ComputeHash(cert.RawData);
            }
        }

        internal HttpClient CreateHttpClient()
        {
            return LndSwaggerClient.CreateHttpClient(_LndSettings);
        }

        internal T Deserialize<T>(string str)
        {
            return JsonConvert.DeserializeObject<T>(str, _settings.Value);
        }
    }
}
