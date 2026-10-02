using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
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

        /// <summary>
        /// Restarts LND and verifies the existing listener reconnects and catches up from its add index.
        /// Requires docker-compose stack running. Run: dotnet test --filter "Category=LndTestListener"
        /// </summary>
        [Fact(Timeout = 90_000)]
        [Trait("Category", "LndTestListener")]
        public async Task ListenReconnectsAfterDockerContainerRestart()
        {
            var rpc = Tester.CreateRPC();
            await rpc.ScanRPCCapabilitiesAsync();
            await rpc.GenerateAsync(1);

            ILightningClient client = Tester.CreateLndClient();
            await WaitForLndReady(client, TimeSpan.FromSeconds(10));

            using var listener = await client.Listen(CancellationToken.None);
            var first = await client.CreateInvoice(LightMoney.Satoshis(1), "Before restart", TimeSpan.FromMinutes(1));
            using (var firstCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                Assert.Equal(first.Id, (await listener.WaitInvoice(firstCts.Token)).Id);

            await RunDockerCompose("restart lnd");
            await WaitForLndReady(client, TimeSpan.FromSeconds(30));

            var second = await client.CreateInvoice(LightMoney.Satoshis(2), "After restart", TimeSpan.FromMinutes(1));
            using var secondCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Assert.Equal(second.Id, (await listener.WaitInvoice(secondCts.Token)).Id);
        }

        /// <summary>
        /// Kills LND and verifies the listener ends after three failed reconnect attempts.
        /// Requires docker-compose stack running. Run: dotnet test --filter "Category=LndTestListener"
        /// </summary>
        [Fact(Timeout = 90_000)]
        [Trait("Category", "LndTestListener")]
        public async Task ListenEndsAfterThreeFailedReconnects()
        {
            var rpc = Tester.CreateRPC();
            await rpc.ScanRPCCapabilitiesAsync();

            // Generate a block so LND considers itself synced
            await rpc.GenerateAsync(5);

            ILightningClient client = Tester.CreateLndClient();
            await WaitForLndReady(client, TimeSpan.FromSeconds(10));

            using (var listener = await client.Listen(CancellationToken.None))
            {
                await RunDockerCompose("kill lnd");

                await AssertWaitInvoiceThrows(listener,
                    "WaitInvoice should have thrown after three failed reconnect attempts");
            }

            // Restore the shared test service.
            await RunDockerCompose("start lnd");
            await rpc.GenerateAsync(1);
            ILightningClient freshClient = Tester.CreateLndClient();
            await WaitForLndReady(freshClient, TimeSpan.FromSeconds(30));

            using var newListener = await freshClient.Listen(CancellationToken.None);
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

        private static async Task AssertWaitInvoiceThrows(ILightningInvoiceListener listener, string message)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var exception = await Record.ExceptionAsync(() => listener.WaitInvoice(cts.Token));
            Assert.NotNull(exception);
            Assert.False(exception is OperationCanceledException, message);
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

        private static async Task RunDockerCompose(string args)
        {
            var testsDir = FindTestsDirectory();
            var psi = new ProcessStartInfo("docker", $"compose {args}")
            {
                WorkingDirectory = testsDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            Assert.NotNull(proc);
            await proc.WaitForExitAsync();
            Assert.True(proc.ExitCode == 0,
                $"docker compose {args} failed (exit {proc.ExitCode}): {await proc.StandardError.ReadToEndAsync()}");
        }

        private static string FindTestsDirectory([CallerFilePath] string sourceFilePath = "")
        {
            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                var dir = Path.GetDirectoryName(sourceFilePath);
                if (dir != null && File.Exists(Path.Combine(dir, "docker-compose.yml")))
                    return dir;
            }

            var current = Path.GetDirectoryName(typeof(LndListenStreamTests).Assembly.Location);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current, "docker-compose.yml")))
                    return current;
                current = Path.GetDirectoryName(current);
            }

            throw new InvalidOperationException("Could not find tests/ directory with docker-compose.yml");
        }

        #endregion
    }
}
