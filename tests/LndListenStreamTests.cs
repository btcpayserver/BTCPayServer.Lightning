using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Lightning.Tests
{
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class LndListenStreamTests(ITestOutputHelper h) : BaseTests(h)
    {
        /// <summary>
        /// Verifies the LND WebSocket subscription receives invoice events.
        /// Requires docker-compose stack running. Run: dotnet test --filter "Category=LndTestListener"
        /// </summary>
        [Fact(Timeout = 90_000)]
        [Trait("Category", "LndTestListener")]
        public async Task ListenReceivesCreatedInvoiceOverWebSocket()
        {
            var rpc = Tester.CreateRPC();
            await rpc.ScanRPCCapabilitiesAsync();
            await rpc.GenerateAsync(1);

            var handler = new WebSocketTrackingHandler(new HttpClientHandler());
            using var httpClient = new HttpClient(handler);
            ILightningClient client = Tester.CreateLndClient(httpClient);
            await WaitForLndReady(client, TimeSpan.FromSeconds(10));

            using var listener = await client.Listen(CancellationToken.None);
            Assert.True(handler.WebSocketRequested);
            var created = await client.CreateInvoice(LightMoney.Satoshis(1), "WebSocket test", TimeSpan.FromMinutes(1));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var received = await listener.WaitInvoice(cts.Token);

            Assert.Equal(created.Id, received.Id);
        }

        [Fact(Timeout = 90_000)]
        [Trait("Category", "LndTestListener")]
        public async Task ListenHonorsCancellationDuringStartup()
        {
            ILightningClient client = Tester.CreateLndClient();
            using var listenCts = new CancellationTokenSource();
            listenCts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Listen(listenCts.Token));
        }

        #region Helpers

        private sealed class WebSocketTrackingHandler : DelegatingHandler
        {
            private int _webSocketRequested;

            public WebSocketTrackingHandler(HttpMessageHandler innerHandler) : base(innerHandler)
            {
            }

            public bool WebSocketRequested => Volatile.Read(ref _webSocketRequested) != 0;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                if (request.Method == HttpMethod.Connect || request.Headers.Contains("Sec-WebSocket-Key"))
                    Interlocked.Exchange(ref _webSocketRequested, 1);
                return base.SendAsync(request, cancellationToken);
            }
        }

        private static async Task WaitForLndReady(ILightningClient client, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            while (true)
            {
                try
                {
                    await client.GetInfo(cts.Token);
                    return;
                }
                catch when (!cts.IsCancellationRequested) { await Task.Delay(1000, cts.Token); }
            }
        }

        #endregion
    }
}
