using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin.RPC;
using Xunit;

namespace BTCPayServer.Lightning.Tests
{
    public class LndListenStreamTests
    {
        /// <summary>
        /// Kills the real LND Docker container and verifies EOF detection + reconnection.
        /// Requires docker-compose stack running. Run: dotnet test --filter "Category=LndTestListener"
        /// </summary>
        [Fact(Timeout = 90_000)]
        [Trait("Category", "LndTestListener")]
        public async Task ListenRecoversAfterDockerContainerKill()
        {
            CommonTests.Docker = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IN_DOCKER_CONTAINER"));

            var rpc = Tester.CreateRPC();
            await rpc.ScanRPCCapabilitiesAsync();

            // Generate a block so LND considers itself synced
            await rpc.GenerateAsync(5);

            ILightningClient client = Tester.CreateLndClient();
            await WaitForLndReady(client, TimeSpan.FromSeconds(10));

            using (var listener = await client.Listen(CancellationToken.None))
            {
                await RunDockerCompose("kill lnd");

                await AssertWaitInvoiceThrowsOnEOF(listener, TimeSpan.FromSeconds(5),
                    "WaitInvoice should have thrown after LND container was killed");
            }

            // Restart and verify reconnection
            await RunDockerCompose("start lnd");
            await rpc.GenerateAsync(1);
            ILightningClient freshClient = Tester.CreateLndClient();
            await WaitForLndReady(freshClient, TimeSpan.FromSeconds(30));

            using var newListener = await freshClient.Listen(CancellationToken.None);
        }

        #region Helpers

        private static async Task AssertWaitInvoiceThrowsOnEOF(
            ILightningInvoiceListener listener, TimeSpan maxElapsed, string message)
        {
            var sw = Stopwatch.StartNew();
            var threw = false;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await listener.WaitInvoice(cts.Token);
            }
            catch
            {
                threw = true;
            }

            sw.Stop();

            Assert.True(threw, message);
            Assert.True(sw.Elapsed < maxElapsed,
                $"WaitInvoice took {sw.Elapsed.TotalSeconds:F1}s — expected < {maxElapsed.TotalSeconds}s. " +
                "If close to 10s, ListenLoop is not detecting EOF.");
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
