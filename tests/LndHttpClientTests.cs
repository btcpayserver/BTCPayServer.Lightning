using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LND;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Lightning.Tests
{
    public class LndHttpClientTests
    {
        [Theory]
        [InlineData("")]
        [InlineData(";allowinsecure=true")]
        [InlineData(";allowinsecure=false")]
        [InlineData(";allowinsecure=invalid")]
        [InlineData(";certthumbprint=c51bb1d402306d0da00e85581b32aa56166bcbab7eb888ff925d7167eb436d06")]
        [InlineData(";certthumbprint=invalid")]
        [InlineData(";certfilepath=/nonexistent/lnd/tls.cert")]
        [InlineData(";allowinsecure=invalid;certthumbprint=invalid;certfilepath=/nonexistent/lnd/tls.cert")]
        public async Task LegacyTlsOptionsDoNotReplaceInjectedHttpClient(string options)
        {
            foreach (var scheme in new[] { "http", "https" })
            {
                var handler = new TrackingHandler();
                using var httpClient = new HttpClient(handler);
                var connectionString = $"type=lnd-rest;server={scheme}://localhost:8080/;macaroon=0102";
                var client = Assert.IsType<LndClient>(new LndConnectionStringHandler(httpClient).Create(
                    connectionString + options, Network.RegTest, out var error));

                Assert.Null(error);
                // Payment streams use this factory too and must keep the injected client.
                Assert.Same(httpClient, client.SwaggerClient.CreateHttpClient());
                await client.SwaggerClient.GetInfoAsync();
                Assert.Equal(new Uri($"{scheme}://localhost:8080/v1/getinfo"), handler.RequestUri);
                Assert.Equal("0102", handler.Macaroon);

                using var socket = client.SwaggerClient.CreateClientWebSocket();
                var wsScheme = scheme == "https" ? "wss" : "ws";
                await Assert.ThrowsAnyAsync<Exception>(() => client.SwaggerClient.ConnectClientWebSocket(
                    socket, new Uri($"{wsScheme}://localhost:8080/v1/invoices/subscribe"), CancellationToken.None));
                Assert.True(handler.WebSocketRequested);
                Assert.Equal("0102", handler.Macaroon);
            }
        }

        [Theory]
        [InlineData("http")]
        [InlineData("https")]
        public void LegacyTlsOptionsAreIgnoredWithoutInjectedHttpClient(string scheme)
        {
            var client = Assert.IsType<LndClient>(new LndConnectionStringHandler().Create(
                $"type=lnd-rest;server={scheme}://localhost:8080/;allowinsecure=invalid;certthumbprint=invalid;certfilepath=/nonexistent/lnd/tls.cert",
                Network.RegTest, out var error));

            Assert.Null(error);
            using var httpClient = client.SwaggerClient.CreateHttpClient();
            using var socket = client.SwaggerClient.CreateClientWebSocket();
            Assert.Null(socket.Options.RemoteCertificateValidationCallback);
            Assert.Equal(new Uri($"{scheme}://localhost:8080/"), client.SwaggerClient._LndSettings.Uri);
        }

        private sealed class TrackingHandler : HttpMessageHandler
        {
            public Uri RequestUri { get; private set; }
            public string Macaroon { get; private set; }
            public bool WebSocketRequested { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestUri = request.RequestUri;
                Macaroon = string.Join(",", request.Headers.GetValues("Grpc-Metadata-macaroon"));
                WebSocketRequested = request.Method == HttpMethod.Connect || request.Headers.Contains("Sec-WebSocket-Key");
                return Task.FromResult(new HttpResponseMessage(WebSocketRequested ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            }
        }
    }
}
