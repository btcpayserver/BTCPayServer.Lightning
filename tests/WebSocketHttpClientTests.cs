using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Eclair;
using BTCPayServer.Lightning.Phoenixd;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Lightning.Tests
{
    public class WebSocketHttpClientTests
    {
        [Fact]
        public async Task EclairListenerUsesInjectedHttpClient()
        {
            var handler = new WebSocketTrackingHandler();
            using var httpClient = new HttpClient(handler);
            var client = new EclairLightningClient(new Uri("http://127.0.0.1:8080/base"), "password", Network.RegTest, httpClient);

            await Assert.ThrowsAnyAsync<Exception>(() => client.Listen());

            AssertWebSocketRequest(handler, "ws://127.0.0.1:8080/base/ws");
        }

        [Fact]
        public async Task PhoenixdListenerUsesInjectedHttpClient()
        {
            var handler = new WebSocketTrackingHandler();
            using var httpClient = new HttpClient(handler);
            var client = new PhoenixdLightningClient(new Uri("http://127.0.0.1:8080/base"), "password", Network.RegTest, httpClient);

            await Assert.ThrowsAnyAsync<Exception>(() => client.Listen());

            AssertWebSocketRequest(handler, "ws://127.0.0.1:8080/base/websocket");
        }

        private static void AssertWebSocketRequest(WebSocketTrackingHandler handler, string expectedUri)
        {
            Assert.True(handler.WebSocketRequested);
            Assert.Equal(expectedUri, handler.RequestUri.AbsoluteUri);
            Assert.Equal("Basic", handler.AuthorizationScheme);
        }

        private sealed class WebSocketTrackingHandler : HttpMessageHandler
        {
            public bool WebSocketRequested { get; private set; }
            public Uri RequestUri { get; private set; }
            public string AuthorizationScheme { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                WebSocketRequested = request.Method == HttpMethod.Connect || request.Headers.Contains("Sec-WebSocket-Key");
                RequestUri = request.RequestUri;
                AuthorizationScheme = request.Headers.Authorization?.Scheme;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }
        }
    }
}
