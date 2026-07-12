using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Lightning.Tests
{
    // These tests exercise the c-lightning JSON-RPC request building without a real
    // lightning backend: a tiny in-process TCP server plays the role of lightningd,
    // captures the outgoing request and replies with a canned response.
    public class CLightningPayTests
    {
        // A zero-amount BOLT11 invoice (from the BOLT11 spec test vectors). Because the
        // invoice carries no amount, CLightningClient forwards PayInvoiceParams.Amount as the
        // amount to pay, and derives the xpay 'maxfee' argument from MaxFeePercent.
        const string ZeroAmountInvoice =
            "lnbc1pvjluezpp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqdpl2pkx2ctnv5sxxmmwwd5kgetjypeh2ursdae8g6twvus8g6rfwvs8qun0dfjkxaq8rkx3yf5tcsyz3d73gafnh3cax9rn449d9p5uxz9ezhhypd0elx87sjle52x86fux2ypatgddc6k63n7erqz25le42c4u4ecky03ylcqca784w";

        [Fact]
        public async Task PayMaxFeePercentIsSentInMilliSatoshi()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            JArray capturedParams = null;
            string capturedMethod = null;

            var serverTask = Task.Run(async () =>
            {
                using var server = await listener.AcceptTcpClientAsync();
                using var ns = server.GetStream();

                using (var reader = new StreamReader(ns, new UTF8Encoding(false), false, 1024, leaveOpen: true))
                using (var jr = new JsonTextReader(reader))
                {
                    var req = await JObject.LoadAsync(jr);
                    capturedMethod = req.Value<string>("method");
                    capturedParams = (JArray)req["params"];
                }

                var preimage = new string('0', 64);
                var resp = new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 0,
                    ["result"] = new JObject
                    {
                        ["destination"] = new Key().PubKey.ToHex(),
                        ["status"] = "complete",
                        ["parts"] = 1,
                        ["payment_preimage"] = preimage,
                        ["amount_msat"] = 100000000L,
                        ["amount_sent_msat"] = 100001000L
                    }
                };
                var bytes = new UTF8Encoding(false).GetBytes(resp.ToString(Formatting.None));
                await ns.WriteAsync(bytes, 0, bytes.Length);
                await ns.FlushAsync();

                // Wait for the client to read the response and close its side before tearing the
                // socket down, so the response is not truncated by a premature dispose.
                var drain = new byte[64];
                try { while (await ns.ReadAsync(drain, 0, drain.Length) > 0) { } }
                catch { /* client closed */ }
            });

            var client = new CLightningClient(new Uri($"tcp://127.0.0.1:{port}"), Network.Main);

            // Pay the amountless invoice for 0.001 BTC (100_000 sat == 100_000_000 msat) with a 1% fee ceiling.
            var payParams = new PayInvoiceParams
            {
                Amount = LightMoney.Satoshis(100_000),
                MaxFeePercent = 1d
            };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await ((ILightningClient)client).Pay(ZeroAmountInvoice, payParams, cts.Token);
            await serverTask.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.Equal("xpay", capturedMethod);
            Assert.NotNull(capturedParams);
            // params: [ invstring, amount_msat, maxfee ]
            Assert.Equal(100_000_000L, capturedParams[1].Value<long>());

            // 1% of 100_000_000 msat == 1_000_000 msat. xpay's 'maxfee' argument is denominated in
            // millisatoshi, so the ceiling must be 1_000_000. Computing the percentage in satoshi and
            // sending it as msat yields 1_000 (1000x too small), rejecting valid payments as too expensive.
            Assert.Equal(1_000_000L, capturedParams[2].Value<long>());
        }
    }
}
