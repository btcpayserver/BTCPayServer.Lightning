using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LND
{
    public class LndException : Exception
    {
        public LndException(string message) : base(message)
        {

        }
        public LndException(LNDError error) : base(error.Message)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            _Error = error;
        }


        private readonly LNDError _Error;
        public LNDError Error
        {
            get
            {
                return _Error;
            }
        }
    }
    public partial class LndSwaggerClient
    {
        internal HttpClient _DefaultHttpClient;
        public LndSwaggerClient(LndRestSettings settings) : this(settings, null)
        {

        }
        public LndSwaggerClient(LndRestSettings settings, HttpClient httpClient)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            _DefaultHttpClient = httpClient;
            _LndSettings = settings;
            _Authentication = settings.CreateLndAuthentication();
            BaseUrl = settings.Uri.AbsoluteUri.TrimEnd('/');
            _httpClient = CreateHttpClient();
            _settings = new System.Lazy<Newtonsoft.Json.JsonSerializerSettings>(() =>
            {
                var json = new Newtonsoft.Json.JsonSerializerSettings();
                UpdateJsonSerializerSettings(json);
                return json;
            });
        }
        internal LndRestSettings _LndSettings;
        internal LndAuthentication _Authentication;

        partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
        {
            _Authentication.AddAuthentication(request);
        }

        internal HttpClient CreateHttpClient()
        {
            return _DefaultHttpClient ?? new HttpClient();
        }

        internal ClientWebSocket CreateClientWebSocket()
        {
            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
#if NET9_0_OR_GREATER
            socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(10);
#endif

            using (var request = new HttpRequestMessage())
            {
                _Authentication.AddAuthentication(request);
                foreach (var header in request.Headers)
                    socket.Options.SetRequestHeader(header.Key, string.Join(",", header.Value));
            }

            return socket;
        }

        internal Task ConnectClientWebSocket(ClientWebSocket socket, Uri uri, CancellationToken cancellationToken)
        {
#if NET7_0_OR_GREATER
            return socket.ConnectAsync(uri, _httpClient, cancellationToken);
#else
            return socket.ConnectAsync(uri, cancellationToken);
#endif
        }

        internal T Deserialize<T>(string str)
        {
            return JsonConvert.DeserializeObject<T>(str, _settings.Value);
        }
    }
}
