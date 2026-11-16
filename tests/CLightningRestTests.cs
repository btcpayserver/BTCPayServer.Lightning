using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.CLightning;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Lightning.Tests
{
    // These tests exercise the clnrest transport without a real node. A fake HttpMessageHandler
    // plays clnrest, and a small TLS server checks certificate pinning.
    public class CLightningRestTests
    {
        const string Rune = "tU-RLjMiDpY2U0o3W1oFowar36RFGpWloPbW9-RuZdo9MA==";
        const string AmountInvoice = CLightningPayTests.AmountInvoice;
        const string AmountInvoiceHash = "0001020304050607080900010203040506070809000102030405060708090102";

        [Fact]
        public async Task SendsNamedParametersAndRune()
        {
            var clnrest = new FakeClnRest(Respond(HttpStatusCode.Created, "{}"));
            var client = CreateClient(clnrest);

            await client.FundChannelAsync(new OpenChannelRequest
            {
                NodeInfo = new NodeInfo(new Key().PubKey, "127.0.0.1", 9735),
                ChannelAmount = Money.Satoshis(100_000),
                FeeRate = new FeeRate(Money.Satoshis(1000))
            }, CancellationToken.None);

            var request = Assert.Single(clnrest.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://127.0.0.1:3010/v1/fundchannel", request.Uri.AbsoluteUri);
            Assert.Equal(Rune, request.Rune);
            Assert.Equal("application/json", request.ContentType);
            var body = JObject.Parse(request.Body);
            Assert.Equal(new[] { "id", "amount", "feerate" }, body.Properties().Select(p => p.Name));
            Assert.Equal(100_000, body["amount"].Value<long>());
            Assert.Equal("1000perkb", body["feerate"].Value<string>());
        }

        [Fact]
        public async Task ReadsRuneFileOnEachRequest()
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.rune");
            try
            {
                File.WriteAllText(runeFile, "first-rune\n");
                var clnrest = new FakeClnRest(Respond(HttpStatusCode.Created, "{}"), Respond(HttpStatusCode.Created, "{}"));
                var client = CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010"))
                {
                    RuneFilePath = runeFile,
                    AllowInsecure = true
                });

                await client.GetInfoAsync();
                File.WriteAllText(runeFile, "second-rune");
                await client.GetInfoAsync();

                Assert.Equal(new[] { "first-rune", "second-rune" }, clnrest.Requests.Select(r => r.Rune));
                Assert.Equal("http://127.0.0.1:3010/v1/getinfo", clnrest.Requests[0].Uri.AbsoluteUri);
            }
            finally
            {
                File.Delete(runeFile);
            }
        }

        // Only the codes that xpay returns before it sends any part are final.
        // Any other code, even from a proxy that forges one, leaves the payout pending.
        [Theory]
        [InlineData(-32602, PayResult.Error)]
        [InlineData(205, PayResult.CouldNotFindRoute)]
        [InlineData(206, PayResult.Error)]
        [InlineData(207, PayResult.Error)]
        [InlineData(210, PayResult.CouldNotFindRoute)]
        [InlineData(203, PayResult.Unknown)]
        [InlineData(-1, PayResult.Unknown)]
        [InlineData(200, PayResult.Unknown)]
        [InlineData(201, PayResult.Unknown)]
        [InlineData(209, PayResult.Unknown)]
        [InlineData(219, PayResult.Unknown)]
        [InlineData(1502, PayResult.Unknown)]
        public async Task PayOnlyFailsWhenNothingWasSent(int code, PayResult expected)
        {
            var body = $"{{\"code\":{code},\"message\":\"Failed\"}}";
            var client = CreateClient(new FakeClnRest(Respond(HttpStatusCode.InternalServerError, body)));

            var response = await ((ILightningClient)client).Pay(AmountInvoice);

            Assert.Equal(expected, response.Result);
        }

        // When the response has no lightningd error code, the payment may or may not have
        // happened. Pay returns PayResult.Unknown, never PayResult.Error, which BTCPay Server
        // would read as "not paid" and retry.
        [Theory]
        [InlineData(HttpStatusCode.InternalServerError, "{\"code\":null,\"message\":\"Could not connect to lightning-rpc\"}", "application/json")]
        [InlineData(HttpStatusCode.InternalServerError, "{\"message\":\"No code\"}", "application/json")]
        [InlineData(HttpStatusCode.InternalServerError, "{\"code\":\"-1\",\"message\":\"Code is a string\"}", "application/json")]
        [InlineData(HttpStatusCode.InternalServerError, "{\"code\":-1.5,\"message\":\"Code is not an integer\"}", "application/json")]
        [InlineData(HttpStatusCode.InternalServerError, "{\"code\":-1}", "application/json")]
        [InlineData(HttpStatusCode.InternalServerError, "[-1]", "application/json")]
        [InlineData(HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>", "text/html")]
        [InlineData(HttpStatusCode.Created, "<html><body>Captive portal</body></html>", "text/html")]
        [InlineData(HttpStatusCode.ServiceUnavailable, "", "text/plain")]
        public async Task PayIsUnknownWhenTheTransportFails(HttpStatusCode status, string body, string mediaType)
        {
            var client = CreateClient(new FakeClnRest(Respond(status, body, mediaType)));

            var response = await ((ILightningClient)client).Pay(AmountInvoice);

            Assert.Equal(PayResult.Unknown, response.Result);
            Assert.Contains($"HTTP {(int)status}", response.ErrorDetail);
        }

        // After SendTimeout, a failed listpays entry can belong to an earlier attempt while the
        // xpay we sent is still in flight. Only a complete payment settles the outcome.
        [Theory]
        [InlineData("failed", PayResult.Unknown)]
        [InlineData("pending", PayResult.Unknown)]
        [InlineData("complete", PayResult.Ok)]
        public async Task PayAfterSendTimeoutOnlyTrustsACompletePayment(string status, PayResult expected)
        {
            var pays = new JObject
            {
                ["pays"] = new JArray(new JObject
                {
                    ["payment_hash"] = AmountInvoiceHash,
                    ["status"] = status,
                    ["amount_msat"] = 250_000_000,
                    ["amount_sent_msat"] = 250_001_000,
                    ["preimage"] = new string('2', 64)
                })
            };
            var clnrest = new FakeClnRest().Then(Hang).Then(_ => Task.FromResult(Respond(HttpStatusCode.Created, pays.ToString())));
            var client = CreateClient(clnrest);

            var response = await ((ILightningClient)client).Pay(AmountInvoice, new PayInvoiceParams { SendTimeout = TimeSpan.FromMilliseconds(300) });

            Assert.Equal(expected, response.Result);
            Assert.Equal(new[] { "xpay", "listpays" }, clnrest.Requests.Select(r => r.Uri.Segments.Last()));
            Assert.Equal(AmountInvoiceHash, JObject.Parse(clnrest.Requests[1].Body)["payment_hash"].Value<string>());
        }

        // A lookup that fails or doesn't parse doesn't settle anything either.
        [Theory]
        [InlineData(HttpStatusCode.InternalServerError, "{\"code\":-1,\"message\":\"Failed\"}")]
        [InlineData(HttpStatusCode.Created, "{\"pays\":[{\"status\":\"complete\",\"payment_hash\":\"zz\"}]}")]
        [InlineData(HttpStatusCode.Created, "{\"pays\":{}}")]
        public async Task PayAfterSendTimeoutIsUnknownWhenTheLookupFails(HttpStatusCode status, string body)
        {
            var clnrest = new FakeClnRest().Then(Hang).Then(_ => Task.FromResult(Respond(status, body)));
            var client = CreateClient(clnrest);

            var response = await ((ILightningClient)client).Pay(AmountInvoice, new PayInvoiceParams { SendTimeout = TimeSpan.FromMilliseconds(300) });

            Assert.Equal(PayResult.Unknown, response.Result);
            Assert.Equal(2, clnrest.Requests.Count);
        }

        // xpay answered 2xx, but the body doesn't make a payment result.
        [Theory]
        [InlineData("{}")]
        [InlineData("{\"payment_preimage\":\"zz\",\"amount_msat\":1,\"amount_sent_msat\":1}")]
        [InlineData("[]")]
        [InlineData("null")]
        public async Task PayIsUnknownWhenTheAnswerDoesNotParse(string body)
        {
            var client = CreateClient(new FakeClnRest(Respond(HttpStatusCode.Created, body)));

            var response = await ((ILightningClient)client).Pay(AmountInvoice);

            Assert.Equal(PayResult.Unknown, response.Result);
            Assert.NotNull(response.ErrorDetail);
        }

        // Reading the rune file happens before anything is sent, so its failure isn't an unknown outcome.
        [Fact]
        public async Task PayThrowsWhenTheRuneFileIsMissing()
        {
            var clnrest = new FakeClnRest(Respond(HttpStatusCode.Created, "{}"));
            var settings = new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"))
            {
                RuneFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.rune"),
                AllowInsecure = true
            };
            var client = CreateClient(clnrest, settings);

            await Assert.ThrowsAsync<FileNotFoundException>(() => ((ILightningClient)client).Pay(AmountInvoice));
            Assert.Empty(clnrest.Requests);
        }

        [Fact]
        public async Task ResponsesOverTheSizeCapAreTransportErrors()
        {
            // A declared length over the cap is refused before the body is read.
            var declared = new StringContent("{}");
            declared.Headers.ContentLength = ClnRestTransport.MaxResponseSize + 1L;
            var pay = await ((ILightningClient)CreateClient(new FakeClnRest(new HttpResponseMessage(HttpStatusCode.Created) { Content = declared }))).Pay(AmountInvoice);
            Assert.Equal(PayResult.Unknown, pay.Result);

            // A body without a length stops being read at the cap.
            var endless = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StreamContent(new EndlessStream()) };
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => CreateClient(new FakeClnRest(endless)).GetInfoAsync());
            Assert.Contains("more than", ex.Message);
        }

        // A server that sends the headers and then nothing. StalledStream ignores the token, like the
        // response stream on .NET Framework, so only disposing the response ends the read.
        [Fact]
        public async Task StalledBodyEndsWithinTheTimeout()
        {
            var settings = new CLightningRestSettings(new Uri("http://127.0.0.1:3010/")) { Rune = Rune, AllowInsecure = true };
            HttpClient Stalling() => new(new FakeClnRest(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StreamContent(new StalledStream()) }))
            {
                Timeout = TimeSpan.FromSeconds(2)
            };

            var watch = Stopwatch.StartNew();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CLightningClient(settings, Network.Main, Stalling()).GetInfoAsync());
            Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(5));

            // The timeout is a transport error, so the payment outcome is unknown.
            watch.Restart();
            var pay = await ((ILightningClient)new CLightningClient(settings, Network.Main, Stalling())).Pay(AmountInvoice);
            Assert.Equal(PayResult.Unknown, pay.Result);
            Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(5));

            // The caller's cancellation still throws, and quickly.
            watch.Restart();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ((ILightningClient)new CLightningClient(settings, Network.Main, Stalling())).Pay(AmountInvoice, cts.Token));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1.5));
        }

        [Fact]
        public async Task PayIsUnknownOnHttpTimeout()
        {
            var clnrest = new FakeClnRest().Then(Hang);
            var httpClient = new HttpClient(clnrest) { Timeout = TimeSpan.FromSeconds(2) };
            var client = new CLightningClient(new CLightningRestSettings(new Uri("http://127.0.0.1:3010/")) { Rune = Rune, AllowInsecure = true }, Network.Main, httpClient);

            var response = await ((ILightningClient)client).Pay(AmountInvoice);

            Assert.Equal(PayResult.Unknown, response.Result);
            Assert.NotNull(response.ErrorDetail);
        }

        [Fact]
        public async Task PayThrowsOnCallerCancellation()
        {
            var client = CreateClient(new FakeClnRest().Then(Hang));
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ((ILightningClient)client).Pay(AmountInvoice, cts.Token));
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden, 1501, CLightningErrorCode.RUNE_NOT_AUTHORIZED)]
        [InlineData(HttpStatusCode.Unauthorized, 1502, CLightningErrorCode.RUNE_NOT_PERMITTED)]
        [InlineData(HttpStatusCode.Unauthorized, 1503, CLightningErrorCode.RUNE_BLACKLISTED)]
        public async Task RuneErrorsKeepTheirCode(HttpStatusCode status, int code, CLightningErrorCode expected)
        {
            var client = CreateClient(new FakeClnRest(Respond(status, $"{{\"code\":{code},\"message\":\"Not authorized\"}}")));

            var ex = await Assert.ThrowsAsync<LightningRPCException>(() => client.GetInfoAsync());
            Assert.Equal(expected, ex.Code);
        }

        [Fact]
        public async Task ListenerStartsAfterTheNewestPayment()
        {
            var invoices = new JObject
            {
                ["invoices"] = new JArray(PaidInvoice("a", 3), PaidInvoice("b", 7), new JObject { ["label"] = "c", ["status"] = "unpaid" })
            };
            var clnrest = new FakeClnRest(
                Respond(HttpStatusCode.Created, invoices.ToString()),
                Respond(HttpStatusCode.Created, PaidInvoice("d", 8).ToString()),
                Respond(HttpStatusCode.Created, PaidInvoice("e", 9).ToString()));
            using var listener = await ((ILightningClient)CreateClient(clnrest)).Listen();

            Assert.Equal("d", (await listener.WaitInvoice(CancellationToken.None)).Id);
            Assert.Equal("e", (await listener.WaitInvoice(CancellationToken.None)).Id);

            Assert.Equal(new[] { "listinvoices", "waitanyinvoice", "waitanyinvoice" }, clnrest.Requests.Select(r => r.Uri.Segments.Last()));
            Assert.Equal(7, JObject.Parse(clnrest.Requests[1].Body)["lastpay_index"].Value<long>());
            Assert.Equal(8, JObject.Parse(clnrest.Requests[2].Body)["lastpay_index"].Value<long>());
        }

        // Each wait has to end before the HttpClient gives up on the request.
        [Theory]
        [InlineData(100, 30)]
        [InlineData(35, 30)]
        [InlineData(20, 15)]
        [InlineData(10, 5)]
        [InlineData(8, 4)]
        [InlineData(4, 2)]
        [InlineData(2, 1)]
        [InlineData(-1, 30)]
        public async Task ListenerWaitsLessThanTheHttpClientTimeout(int httpTimeoutSeconds, int expected)
        {
            var clnrest = new FakeClnRest(
                Respond(HttpStatusCode.Created, "{\"invoices\":[]}"),
                Respond(HttpStatusCode.Created, PaidInvoice("a", 1).ToString()));
            var httpClient = new HttpClient(clnrest)
            {
                Timeout = httpTimeoutSeconds < 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(httpTimeoutSeconds)
            };
            var settings = new CLightningRestSettings(new Uri("http://127.0.0.1:3010/")) { Rune = Rune, AllowInsecure = true };
            using var listener = await ((ILightningClient)new CLightningClient(settings, Network.Main, httpClient)).Listen();

            await listener.WaitInvoice(CancellationToken.None);

            Assert.Equal(expected, JObject.Parse(clnrest.Requests[1].Body)["timeout"].Value<int>());
        }

        [Fact]
        public void RejectsAnHttpClientTimeoutUnderTwoSeconds()
        {
            var settings = new CLightningRestSettings(new Uri("http://127.0.0.1:3010/")) { Rune = Rune, AllowInsecure = true };
            var httpClient = new HttpClient(new FakeClnRest()) { Timeout = TimeSpan.FromSeconds(1.5) };

            var ex = Assert.Throws<ArgumentException>(() => new CLightningClient(settings, Network.Main, httpClient));
            Assert.Contains("at least 2 seconds", ex.Message);
        }

        [Fact]
        public async Task ListenerRetriesTransportFailures()
        {
            var clnrest = new FakeClnRest(
                Respond(HttpStatusCode.Created, "{\"invoices\":[]}"),
                Respond(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>", "text/html"),
                Respond(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>", "text/html"),
                Respond(HttpStatusCode.Created, PaidInvoice("a", 1).ToString()));
            using var listener = await ((ILightningClient)CreateClient(clnrest)).Listen();

            Assert.Equal("a", (await listener.WaitInvoice(CancellationToken.None)).Id);

            var waits = clnrest.Requests.Skip(1).ToArray();
            Assert.Equal(3, waits.Length);
            // It waits 1 second after the first failure, then 2 seconds.
            Assert.True(waits[1].Time - waits[0].Time >= TimeSpan.FromMilliseconds(900));
            Assert.True(waits[2].Time - waits[1].Time >= TimeSpan.FromMilliseconds(1900));
        }

        [Fact]
        public async Task ListenerRetriesAnswersThatDoNotParse()
        {
            var clnrest = new FakeClnRest(
                Respond(HttpStatusCode.Created, "{\"invoices\":[]}"),
                Respond(HttpStatusCode.Created, "{\"pay_index\":\"not a number\"}"),
                Respond(HttpStatusCode.Created, PaidInvoice("a", 1).ToString()));
            using var listener = await ((ILightningClient)CreateClient(clnrest)).Listen();

            Assert.Equal("a", (await listener.WaitInvoice(CancellationToken.None)).Id);
            Assert.Equal(3, clnrest.Requests.Count);
        }

        [Fact]
        public async Task ListenerRetriesWhenTheRuneFileCannotBeRead()
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.rune");
            try
            {
                File.WriteAllText(runeFile, Rune);
                var clnrest = new FakeClnRest(
                    Respond(HttpStatusCode.Created, "{\"invoices\":[]}"),
                    Respond(HttpStatusCode.Created, PaidInvoice("a", 1).ToString()));
                var client = CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/")) { RuneFilePath = runeFile, AllowInsecure = true });
                using var listener = await ((ILightningClient)client).Listen();

                // The first wait fails with an IOException before it sends anything.
                File.Delete(runeFile);
                var wait = listener.WaitInvoice(CancellationToken.None);
                await Task.Delay(300);
                File.WriteAllText(runeFile, Rune);

                Assert.Equal("a", (await wait).Id);
                Assert.Equal(2, clnrest.Requests.Count);
            }
            finally
            {
                File.Delete(runeFile);
            }
        }

        [Fact]
        public async Task ListenerRetriesAnInvoiceThatDoesNotConvert()
        {
            var unknownStatus = PaidInvoice("a", 1);
            unknownStatus["status"] = "something new";
            var clnrest = new FakeClnRest(
                Respond(HttpStatusCode.Created, "{\"invoices\":[]}"),
                Respond(HttpStatusCode.Created, unknownStatus.ToString()),
                Respond(HttpStatusCode.Created, PaidInvoice("a", 1).ToString()));
            using var listener = await ((ILightningClient)CreateClient(clnrest)).Listen();

            Assert.Equal("a", (await listener.WaitInvoice(CancellationToken.None)).Id);

            // The cursor stayed put, so the second wait asks for the same invoice again.
            var waits = clnrest.Requests.Skip(1).Select(r => JObject.Parse(r.Body)["lastpay_index"].Value<long>());
            Assert.Equal(new long[] { 0, 0 }, waits);
        }

        [Fact]
        public async Task ListenerStopsOnLightningdErrors()
        {
            var clnrest = new FakeClnRest(
                Respond(HttpStatusCode.Created, "{\"invoices\":[]}"),
                Respond(HttpStatusCode.Unauthorized, "{\"code\":1502,\"message\":\"Not permitted\"}"));
            using var listener = await ((ILightningClient)CreateClient(clnrest)).Listen();

            var ex = await Assert.ThrowsAsync<LightningRPCException>(() => listener.WaitInvoice(CancellationToken.None));
            Assert.Equal(CLightningErrorCode.RUNE_NOT_PERMITTED, ex.Code);
        }

        [Fact]
        public async Task ListenerRetriesTimeoutsWithoutSpinning()
        {
            var clnrest = new FakeClnRest(
                Respond(HttpStatusCode.Created, "{\"invoices\":[]}"),
                Respond(HttpStatusCode.InternalServerError, "{\"code\":904,\"message\":\"Timed out\"}"),
                Respond(HttpStatusCode.Created, PaidInvoice("a", 1).ToString()));
            using var listener = await ((ILightningClient)CreateClient(clnrest)).Listen();

            Assert.Equal("a", (await listener.WaitInvoice(CancellationToken.None)).Id);

            var waits = clnrest.Requests.Skip(1).ToArray();
            Assert.Equal(2, waits.Length);
            foreach (var request in waits)
            {
                Assert.Equal("http://127.0.0.1:3010/v1/waitanyinvoice", request.Uri.AbsoluteUri);
                var body = JObject.Parse(request.Body);
                Assert.Equal(0, body["lastpay_index"].Value<long>());
                Assert.Equal(30, body["timeout"].Value<int>());
            }
            // The 904 came back at once, so the listener waits before it asks again.
            Assert.True(waits[1].Time - waits[0].Time >= TimeSpan.FromMilliseconds(900));
        }

        [Fact]
        public async Task DisposingTheListenerCancelsTheWait()
        {
            var clnrest = new FakeClnRest(Respond(HttpStatusCode.Created, "{\"invoices\":[]}")).Then(Hang);
            var listener = await ((ILightningClient)CreateClient(clnrest)).Listen();

            var wait = listener.WaitInvoice(CancellationToken.None);
            await Task.Delay(100);
            listener.Dispose();

            await Assert.ThrowsAsync<OperationCanceledException>(() => wait);
        }

        [Fact]
        public async Task PinnedThumbprintAcceptsOnlyThatCertificate()
        {
            using var server = new TlsServer();
            var thumbprint = SHA256.HashData(server.Certificate.RawData);
            // The pin needs its own handler, so the injected client must not be used.
            var injected = new FakeClnRest();

            var info = await CreatePinnedClient(server, thumbprint, injected).GetInfoAsync();

            Assert.NotNull(info);
            Assert.Empty(injected.Requests);
            var wrong = RandomNumberGenerator.GetBytes(32);
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => CreatePinnedClient(server, wrong, injected).GetInfoAsync());
            Assert.IsType<AuthenticationException>(ex.InnerException);
        }

        [Fact]
        public async Task PinnedThumbprintWinsOverAllowInsecure()
        {
            using var server = new TlsServer();
            var settings = new CLightningRestSettings(server.Uri)
            {
                Rune = Rune,
                CertificateThumbprint = RandomNumberGenerator.GetBytes(32),
                AllowInsecure = true
            };

            await Assert.ThrowsAsync<HttpRequestException>(() => new CLightningClient(settings, Network.Main).GetInfoAsync());
        }

        [Fact]
        public async Task CertificateFileAcceptsOnlyThatCertificate()
        {
            using var server = new TlsServer();
            var certFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pem");
            try
            {
                File.WriteAllText(certFile, server.Certificate.ExportCertificatePem());
                var connectionString = $"type=clightning;server={server.Uri};rune={Rune};certfilepath={certFile}";
                var client = Assert.IsType<CLightningClient>(new CLightningConnectionStringHandler().Create(connectionString, Network.Main, out var error));
                Assert.Null(error);
                Assert.Equal(connectionString, client.ToString());
                Assert.NotNull(await client.GetInfoAsync());

                // The client read the file when it was created, a new client reads it again.
                using var other = TlsServer.CreateCertificate();
                File.WriteAllText(certFile, other.ExportCertificatePem());
                Assert.NotNull(await client.GetInfoAsync());
                var settings = new CLightningRestSettings(server.Uri) { Rune = Rune, CertificateFilePath = certFile };
                await Assert.ThrowsAsync<HttpRequestException>(() => new CLightningClient(settings, Network.Main).GetInfoAsync());
            }
            finally
            {
                File.Delete(certFile);
            }
        }

        [Fact]
        public void CertificateFileHasASizeCap()
        {
            using var certificate = TlsServer.CreateCertificate();
            var certFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pem");
            try
            {
                var pem = certificate.ExportCertificatePem();
                File.WriteAllText(certFile, pem + new string('\n', ClnRestTransport.MaxCertificateFileSize - pem.Length));
                var settings = new CLightningRestSettings(new Uri("https://127.0.0.1:3010/")) { Rune = Rune, CertificateFilePath = certFile };
                Assert.NotNull(new CLightningClient(settings, Network.Main));

                File.AppendAllText(certFile, "\n");
                var ex = Assert.Throws<InvalidOperationException>(() => new CLightningClient(settings, Network.Main));
                Assert.Contains("bigger than", ex.Message);
            }
            finally
            {
                File.Delete(certFile);
            }
        }

        [Theory]
        [InlineData("https://127.0.0.1:3010/clnrest")]
        [InlineData("https://127.0.0.1:3010/?")]
        [InlineData("https://127.0.0.1:3010/#")]
        [InlineData("https://127.0.0.1:3010/x?a=b#c")]
        public void RejectsServerWithPathQueryOrFragment(string server)
        {
            var settings = new CLightningRestSettings(new Uri(server)) { Rune = Rune };

            Assert.Throws<ArgumentException>(() => new CLightningClient(settings, Network.Main));
        }

        // The response can come from any service, so error messages don't carry its body.
        [Theory]
        [InlineData(HttpStatusCode.InternalServerError, "{\"secret\":\"internal-data\"}", "application/json")]
        [InlineData(HttpStatusCode.InternalServerError, "{\"code\":null,\"message\":\"internal-data\"}", "application/json")]
        [InlineData(HttpStatusCode.Forbidden, "<html>internal-data</html>", "text/html")]
        [InlineData(HttpStatusCode.OK, "internal-data", "text/plain")]
        public async Task ErrorsDoNotEchoTheResponseBody(HttpStatusCode status, string body, string mediaType)
        {
            var client = CreateClient(new FakeClnRest(Respond(status, body, mediaType)));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetInfoAsync());
            Assert.DoesNotContain("internal-data", ex.Message);
            Assert.Contains(((int)status).ToString(), ex.Message);
        }

        [Fact]
        public async Task LongLightningdMessagesAreCut()
        {
            var message = new string('x', 500);
            var client = CreateClient(new FakeClnRest(Respond(HttpStatusCode.InternalServerError, $"{{\"code\":-1,\"message\":\"{message}\"}}")));

            var ex = await Assert.ThrowsAsync<LightningRPCException>(() => client.GetInfoAsync());
            Assert.Equal(CLightningErrorCode.GENERAL_ERROR, ex.Code);
            Assert.Equal(new string('x', ClnRestTransport.MaxErrorMessageLength) + "...", ex.Message);
        }

        [Fact]
        public void RejectsPinsOnHttp()
        {
            var server = new Uri("http://10.10.0.2:3010/");
            var thumbprint = new CLightningRestSettings(server) { Rune = Rune, AllowInsecure = true, CertificateThumbprint = new byte[32] };
            var file = new CLightningRestSettings(server) { Rune = Rune, AllowInsecure = true, CertificateFilePath = "/tmp/server.pem" };

            Assert.Throws<ArgumentException>(() => new CLightningClient(thumbprint, Network.Main));
            Assert.Throws<ArgumentException>(() => new CLightningClient(file, Network.Main));
        }

        [Fact]
        public void RejectsHttpWithoutAllowInsecure()
        {
            var settings = new CLightningRestSettings(new Uri("http://10.10.0.2:3010/")) { Rune = Rune };

            Assert.Throws<InvalidOperationException>(() => new CLightningClient(settings, Network.Main));
        }

        [Fact]
        public async Task UnpinnedHttpsUsesInjectedHttpClient()
        {
            var clnrest = new FakeClnRest(Respond(HttpStatusCode.Created, "{}"));
            var client = new CLightningClient(new CLightningRestSettings(new Uri("https://127.0.0.1:3010/")) { Rune = Rune }, Network.Main, new HttpClient(clnrest));

            await client.GetInfoAsync();

            Assert.Single(clnrest.Requests);
        }

        [Fact]
        public async Task UnpinnedHttpsRejectsUntrustedCertificate()
        {
            using var server = new TlsServer();
            var client = new CLightningClient(new CLightningRestSettings(server.Uri) { Rune = Rune }, Network.Main);

            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetInfoAsync());
        }

        [Theory]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/", "The key 'rune' or 'runefilepath' is mandatory for clnrest")]
        [InlineData("type=clightning;server=http://10.10.0.2:3010/;rune=abc;allowinsecure=true;certthumbprint=c51bb1d402306d0da00e85581b32aa56166bcbab7eb888ff925d7167eb436d06", "The keys 'certthumbprint' and 'certfilepath' need an https:// server")]
        [InlineData("type=clightning;server=http://10.10.0.2:3010/;rune=abc;allowinsecure=true;certfilepath=/tmp/server.pem", "The keys 'certthumbprint' and 'certfilepath' need an https:// server")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/clnrest;rune=abc", "The key 'server' should be the root URL of clnrest, without a path, query or fragment")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/?;rune=abc", "The key 'server' should be the root URL of clnrest, without a path, query or fragment")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/#;rune=abc", "The key 'server' should be the root URL of clnrest, without a path, query or fragment")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010?a=b;rune=abc", "The key 'server' should be the root URL of clnrest, without a path, query or fragment")]
        [InlineData("type=clightning;server=https://internal:8443/admin/rotate-keys?;rune=abc", "The key 'server' should be the root URL of clnrest, without a path, query or fragment")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/;rune=abc;runefilepath=/tmp/a.rune", "The key 'rune' is already specified")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/;runefilepath=/etc/passwd", "The key 'runefilepath' should point to a .rune file")]
        [InlineData("type=clightning;server=tcp://127.0.0.1:9835/;rune=abc", "The keys 'rune' and 'runefilepath' need a clnrest server starting by http:// or https://")]
        [InlineData("type=clightning;server=unix://root/.lightning/lightning-rpc;runefilepath=/tmp/a.rune", "The keys 'rune' and 'runefilepath' need a clnrest server starting by http:// or https://")]
        [InlineData("type=clightning;server=http://10.10.0.2:3010/;rune=abc", "The key 'allowinsecure' is false, but server's Uri is not using https")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/;rune=abc;allowinsecure=yes", "The key 'allowinsecure' should be true or false")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/;rune=abc;certthumbprint=zz", "The key 'certthumbprint' has invalid format: it should be the SHA256 of the certificate")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/;rune=abc;certthumbprint=abcd", "The key 'certthumbprint' has invalid length: it should be the SHA256 of the certificate (32 bytes)")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/;rune=abc;certthumbprint=c51bb1d402306d0da00e85581b32aa56166bcbab7eb888ff925d7167eb436d06;certfilepath=/tmp/server.pem", "The key 'certfilepath' conflict with 'certthumbprint'")]
        public void RejectsInvalidClnRestConnectionStrings(string connectionString, string expectedError)
        {
            var client = new CLightningConnectionStringHandler().Create(connectionString, Network.RegTest, out var error);

            Assert.Null(client);
            Assert.Equal(expectedError, error);
        }

        static CLightningClient CreateClient(FakeClnRest clnrest, CLightningRestSettings settings = null)
        {
            settings ??= new CLightningRestSettings(new Uri("http://127.0.0.1:3010/")) { Rune = Rune, AllowInsecure = true };
            return new CLightningClient(settings, Network.Main, new HttpClient(clnrest));
        }

        static CLightningClient CreatePinnedClient(TlsServer server, byte[] thumbprint, FakeClnRest injected)
        {
            var settings = new CLightningRestSettings(server.Uri) { Rune = Rune, CertificateThumbprint = thumbprint };
            return new CLightningClient(settings, Network.Main, new HttpClient(injected));
        }

        static HttpResponseMessage Respond(HttpStatusCode status, string body, string mediaType = "application/json")
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
        }

        static async Task<HttpResponseMessage> Hang(CancellationToken cancellation)
        {
            await Task.Delay(Timeout.Infinite, cancellation);
            return null;
        }

        static JObject PaidInvoice(string label, int payIndex)
        {
            return new JObject
            {
                ["label"] = label,
                ["status"] = "paid",
                ["pay_index"] = payIndex,
                ["payment_hash"] = new string('1', 64),
                ["amount_msat"] = 1000,
                ["amount_received_msat"] = 1000,
                ["expires_at"] = 1700000000,
                ["paid_at"] = 1690000000
            };
        }

        // Sends nothing and ignores the token. A read only ends when the stream is disposed.
        sealed class StalledStream : Stream
        {
            readonly TaskCompletionSource<int> _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => _disposed.Task.GetAwaiter().GetResult();
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _disposed.Task;
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(_disposed.Task);
            protected override void Dispose(bool disposing)
            {
                _disposed.TrySetException(new ObjectDisposedException(nameof(StalledStream)));
                base.Dispose(disposing);
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        // Never ends, like a server that keeps sending.
        sealed class EndlessStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count)
            {
                Array.Fill(buffer, (byte)' ', offset, count);
                return count;
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        record CapturedRequest(HttpMethod Method, Uri Uri, string Rune, string ContentType, string Body, DateTimeOffset Time);

        // Records each request and answers with the next canned response.
        sealed class FakeClnRest : HttpMessageHandler
        {
            readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();

            public FakeClnRest(params HttpResponseMessage[] responses)
            {
                foreach (var response in responses)
                    _responses.Enqueue(_ => Task.FromResult(response));
            }

            public FakeClnRest Then(Func<CancellationToken, Task<HttpResponseMessage>> response)
            {
                _responses.Enqueue(response);
                return this;
            }

            public List<CapturedRequest> Requests { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(new CapturedRequest(
                    request.Method,
                    request.RequestUri,
                    request.Headers.TryGetValues("rune", out var rune) ? rune.Single() : null,
                    request.Content?.Headers.ContentType?.MediaType,
                    request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                    DateTimeOffset.UtcNow));
                return await _responses.Dequeue()(cancellationToken);
            }
        }

        // An HTTPS server with a self-signed certificate. It answers every request with 201 and "{}".
        sealed class TlsServer : IDisposable
        {
            readonly TcpListener _listener = new(IPAddress.Loopback, 0);

            public TlsServer()
            {
                Certificate = CreateCertificate();
                _listener.Start();
                Uri = new Uri($"https://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
                _ = AcceptAsync();
            }

            public X509Certificate2 Certificate { get; }
            public Uri Uri { get; }

            public static X509Certificate2 CreateCertificate()
            {
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var request = new CertificateRequest("CN=cln rest server", key, HashAlgorithmName.SHA256);
                using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
                return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
            }

            async Task AcceptAsync()
            {
                while (true)
                {
                    TcpClient tcp;
                    try
                    {
                        tcp = await _listener.AcceptTcpClientAsync();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        return;
                    }
                    _ = ServeAsync(tcp);
                }
            }

            async Task ServeAsync(TcpClient tcp)
            {
                using (tcp)
                using (var ssl = new SslStream(tcp.GetStream()))
                {
                    try
                    {
                        await ssl.AuthenticateAsServerAsync(Certificate);
                        await ReadRequestAsync(ssl);
                        var response = "HTTP/1.1 201 Created\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}";
                        await ssl.WriteAsync(Encoding.ASCII.GetBytes(response));
                    }
                    catch (Exception e) when (e is IOException or AuthenticationException)
                    {
                        // The client rejected the certificate.
                    }
                }
            }

            static async Task ReadRequestAsync(Stream stream)
            {
                var received = new MemoryStream();
                var buffer = new byte[4096];
                while (true)
                {
                    var read = await stream.ReadAsync(buffer);
                    if (read == 0)
                        return;
                    received.Write(buffer, 0, read);
                    var text = Encoding.ASCII.GetString(received.ToArray());
                    var headersEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (headersEnd < 0)
                        continue;
                    var contentLength = text.Substring(0, headersEnd).Split("\r\n")
                        .Where(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        .Select(line => int.Parse(line.Substring("Content-Length:".Length).Trim()))
                        .FirstOrDefault();
                    if (received.Length >= headersEnd + 4 + contentLength)
                        return;
                }
            }

            public void Dispose()
            {
                _listener.Stop();
                Certificate.Dispose();
            }
        }
    }
}
