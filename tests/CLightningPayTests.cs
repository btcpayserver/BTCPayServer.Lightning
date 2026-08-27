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

        // An amount-carrying BOLT11 invoice (BOLT11 spec test vector, "1 cup coffee"):
        // 2500 microBTC == 250_000 sat == 250_000_000 msat.
        const string AmountInvoice =
            "lnbc2500u1pvjluezpp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqdq5xysxxatsyp3k7enxv4jsxqzpuaztrnwngzn3kdzw5hydlzf03qdgm2hdq27cqv3agm2awhz5se903vruatfhq77w3ls4evs3ch9zw97j25emudupq63nyw24cg27h2rspfj9srp";

        // Stands up a fake lightningd, runs the pay, and returns the captured JSON-RPC request.
        static async Task<(string Method, JArray Params)> CapturePayRequest(
            string bolt11, PayInvoiceParams payParams, long amountMsat, long amountSentMsat)
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
                        ["amount_msat"] = amountMsat,
                        ["amount_sent_msat"] = amountSentMsat
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await ((ILightningClient)client).Pay(bolt11, payParams, cts.Token);
            await serverTask.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.NotNull(capturedParams);
            return (capturedMethod, capturedParams);
        }

        [Fact]
        public async Task PayMaxFeePercentIsSentInMilliSatoshi()
        {
            // Pay the amountless invoice for 0.001 BTC (100_000 sat == 100_000_000 msat) with a 1% fee ceiling.
            var payParams = new PayInvoiceParams
            {
                Amount = LightMoney.Satoshis(100_000),
                MaxFeePercent = 1d
            };
            var (method, prms) = await CapturePayRequest(ZeroAmountInvoice, payParams, 100000000L, 100001000L);

            Assert.Equal("xpay", method);
            // params: [ invstring, amount_msat, maxfee ]
            Assert.Equal(100_000_000L, prms[1].Value<long>());

            // 1% of 100_000_000 msat == 1_000_000 msat. xpay's 'maxfee' argument is denominated in
            // millisatoshi, so the ceiling must be 1_000_000. Computing the percentage in satoshi and
            // sending it as msat yields 1_000 (1000x too small), rejecting valid payments as too expensive.
            Assert.Equal(1_000_000L, prms[2].Value<long>());
        }

        [Fact]
        public async Task PayMaxFeePercentIsAppliedToInvoiceAmount()
        {
            // The common case: the BOLT11 already carries the amount, so PayInvoiceParams.Amount is
            // not forwarded. MaxFeePercent must still be honoured, derived from the invoice amount.
            var payParams = new PayInvoiceParams { MaxFeePercent = 1d };
            var (method, prms) = await CapturePayRequest(AmountInvoice, payParams, 250000000L, 252500000L);

            Assert.Equal("xpay", method);
            // params: [ invstring, amount_msat, maxfee ]
            // amount_msat stays null: xpay only accepts it for amountless invoices.
            Assert.Equal(JTokenType.Null, prms[1].Type);

            // 1% of the invoice's 250_000_000 msat == 2_500_000 msat. When 'maxfee' is omitted, xpay
            // falls back to its own default of "5000msat, or 1% (whatever is greater)", so the fee
            // ceiling configured by the user is silently not applied.
            Assert.Equal(2_500_000L, prms[2].Value<long>());
        }

        [Fact]
        public async Task PayMaxFeePercentKeepsExemptFeeFloorOnSmallPayments()
        {
            // The legacy 'pay' command left 'exemptfee' at 5000msat, so a small payment was accepted
            // when its fee was below 5000msat even if that exceeded the percentage. xpay's 'maxfee'
            // overrides exemptfee, so the floor has to be folded in or small payments get a ceiling
            // an order of magnitude tighter than before.
            var payParams = new PayInvoiceParams
            {
                Amount = LightMoney.Satoshis(100),
                MaxFeePercent = 0.5d
            };
            var (method, prms) = await CapturePayRequest(ZeroAmountInvoice, payParams, 100000L, 105000L);

            Assert.Equal("xpay", method);
            Assert.Equal(100_000L, prms[1].Value<long>());

            // 0.5% of 100_000 msat is only 500 msat; exemptfee's 5000msat floor wins.
            Assert.Equal(5_000L, prms[2].Value<long>());
        }
    }
}
