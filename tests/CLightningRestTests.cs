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
        const string RotatedRune = "7cKJyALVY0_LLVV-AB9oetXjipOdyt0EhOuYrSS42fM9MA==";
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

        [Theory]
        [InlineData(1501)]
        [InlineData(1502)]
        [InlineData(1503)]
        public async Task ReloadsRuneFileOnceAfterRuneRejection(int code)
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
            try
            {
                File.WriteAllText(runeFile, Rune + "\n");
                var clnrest = new FakeClnRest(
                    Respond(HttpStatusCode.Created, "{}"),
                    Respond(HttpStatusCode.Unauthorized, $"{{\"code\":{code},\"message\":\"Rejected\"}}"),
                    Respond(HttpStatusCode.Created, "{}"));
                using var httpClient = new HttpClient(clnrest);
                var client = Assert.IsType<CLightningClient>(new CLightningConnectionStringHandler(httpClient).Create(
                    $"type=clightning;server=http://127.0.0.1:3010/;runefilepath={runeFile}",
                    Network.Main, out var error));
                Assert.Null(error);

                await client.GetInfoAsync();
                File.WriteAllText(runeFile, RotatedRune);
                await client.GetInfoAsync();

                Assert.Equal(new[] { Rune, Rune, RotatedRune }, clnrest.Requests.Select(r => r.Rune));
                Assert.Equal("http://127.0.0.1:3010/v1/getinfo", clnrest.Requests[0].Uri.AbsoluteUri);
            }
            finally
            {
                File.Delete(runeFile);
            }
        }

        [Fact]
        public async Task CachedRuneDoesNotReadTheFileOnEachRequest()
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
            try
            {
                File.WriteAllText(runeFile, Rune);
                var clnrest = new FakeClnRest(Respond(HttpStatusCode.Created, "{}"), Respond(HttpStatusCode.Created, "{}"));
                var client = CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"))
                {
                    RuneFilePath = runeFile
                });
                await client.GetInfoAsync();
                File.Delete(runeFile);
                await client.GetInfoAsync();
                Assert.Equal(new[] { Rune, Rune }, clnrest.Requests.Select(r => r.Rune));
            }
            finally
            {
                File.Delete(runeFile);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RuneReloadRetriesAtMostOnceAndOnlyIfChanged(bool changed)
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
            try
            {
                File.WriteAllText(runeFile, Rune);
                var clnrest = new FakeClnRest(
                    Respond(HttpStatusCode.Created, "{}"),
                    Respond(HttpStatusCode.Unauthorized, "{\"code\":1503,\"message\":\"Rejected\"}"),
                    Respond(HttpStatusCode.Unauthorized, "{\"code\":1503,\"message\":\"Rejected again\"}"));
                var client = CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"))
                {
                    RuneFilePath = runeFile
                });
                await client.GetInfoAsync();
                if (changed)
                    File.WriteAllText(runeFile, RotatedRune);
                var ex = await Assert.ThrowsAsync<LightningRPCException>(() => client.GetInfoAsync());
                Assert.Equal(CLightningErrorCode.RUNE_BLACKLISTED, ex.Code);
                Assert.Equal(changed ? new[] { Rune, Rune, RotatedRune } : new[] { Rune, Rune },
                    clnrest.Requests.Select(r => r.Rune));
            }
            finally
            {
                File.Delete(runeFile);
            }
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, "{\"message\":\"Rejected\"}")]
        [InlineData(HttpStatusCode.Forbidden, "{\"code\":\"1503\",\"message\":\"Rejected\"}")]
        [InlineData(HttpStatusCode.BadGateway, "<html>Bad Gateway</html>")]
        [InlineData(HttpStatusCode.InternalServerError, "{\"code\":-1,\"message\":\"Failed\"}")]
        [InlineData(HttpStatusCode.OK, "not JSON")]
        public async Task NonRuneErrorsDoNotReloadOrRetry(HttpStatusCode status, string body)
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
            try
            {
                File.WriteAllText(runeFile, Rune);
                var clnrest = new FakeClnRest(Respond(HttpStatusCode.Created, "{}"), Respond(status, body));
                var client = CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"))
                {
                    RuneFilePath = runeFile
                });
                await client.GetInfoAsync();
                File.WriteAllText(runeFile, RotatedRune);
                Assert.NotNull(await Record.ExceptionAsync(() => client.GetInfoAsync()));
                Assert.Equal(new[] { Rune, Rune }, clnrest.Requests.Select(r => r.Rune));
            }
            finally
            {
                File.Delete(runeFile);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TransportFailureDoesNotReloadOrRetry(bool timeout)
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
            try
            {
                File.WriteAllText(runeFile, Rune);
                var clnrest = new FakeClnRest(Respond(HttpStatusCode.Created, "{}"))
                    .Then(_ => Task.FromException<HttpResponseMessage>(
                        timeout ? new TaskCanceledException() : new HttpRequestException("Disconnected")));
                var client = CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"))
                {
                    RuneFilePath = runeFile
                });
                await client.GetInfoAsync();
                File.Delete(runeFile);
                var ex = await Record.ExceptionAsync(() => client.GetInfoAsync());
                if (timeout)
                    Assert.IsAssignableFrom<OperationCanceledException>(ex);
                else
                    Assert.IsType<HttpRequestException>(ex);
                Assert.Equal(new[] { Rune, Rune }, clnrest.Requests.Select(r => r.Rune));
            }
            finally
            {
                File.Delete(runeFile);
            }
        }

        [Fact]
        public async Task ReloadValidatesTheNewRuneBeforeRetrying()
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
            try
            {
                File.WriteAllText(runeFile, Rune);
                var clnrest = new FakeClnRest(
                    Respond(HttpStatusCode.Created, "{}"),
                    Respond(HttpStatusCode.Unauthorized, "{\"code\":1503,\"message\":\"Rejected\"}"));
                var client = CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"))
                {
                    RuneFilePath = runeFile
                });
                await client.GetInfoAsync();
                File.WriteAllText(runeFile, "invalid");
                await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetInfoAsync());
                Assert.Equal(new[] { Rune, Rune }, clnrest.Requests.Select(r => r.Rune));
            }
            finally
            {
                File.Delete(runeFile);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ConcurrentRejectionsShareTheReloadedRune(bool changed)
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
            var firstRejected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondRejected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                File.WriteAllText(runeFile, Rune);
                var clnrest = new FakeClnRest(Respond(HttpStatusCode.Created, "{}"))
                    .Then(async token =>
                    {
                        await firstRejected.Task.WaitAsync(token);
                        return Respond(HttpStatusCode.Unauthorized, "{\"code\":1503,\"message\":\"Rejected\"}");
                    })
                    .Then(async token =>
                    {
                        await secondRejected.Task.WaitAsync(token);
                        return Respond(HttpStatusCode.Unauthorized, "{\"code\":1503,\"message\":\"Rejected\"}");
                    })
                    .Then(_ => Task.FromResult(Respond(HttpStatusCode.Created, "{}")))
                    .Then(_ => Task.FromResult(Respond(HttpStatusCode.Created, "{}")));
                var client = CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"))
                {
                    RuneFilePath = runeFile
                });
                await client.GetInfoAsync(cancellation.Token);
                var first = client.GetInfoAsync(cancellation.Token);
                var second = client.GetInfoAsync(cancellation.Token);
                if (changed)
                    File.WriteAllText(runeFile, RotatedRune);
                firstRejected.SetResult(true);
                if (changed)
                    await first;
                else
                    await Assert.ThrowsAsync<LightningRPCException>(() => first);

                // The other in-flight rejection must share the first reload,
                // even if the reloaded string has the same value.
                File.Delete(runeFile);
                secondRejected.SetResult(true);
                if (changed)
                    await second;
                else
                    await Assert.ThrowsAsync<LightningRPCException>(() => second);
                Assert.Equal(changed ? new[] { Rune, Rune, Rune, RotatedRune, RotatedRune } : new[] { Rune, Rune, Rune },
                    clnrest.Requests.Select(r => r.Rune));
            }
            finally
            {
                File.Delete(runeFile);
            }
        }

        [Fact]
        public async Task PayRetriesOnlyTheExplicitlyRejectedRequestWithTheNewRune()
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
            try
            {
                File.WriteAllText(runeFile, Rune);
                var clnrest = new FakeClnRest()
                    .Then(_ =>
                    {
                        File.WriteAllText(runeFile, RotatedRune);
                        return Task.FromResult(Respond(HttpStatusCode.Unauthorized,
                            "{\"code\":1503,\"message\":\"Rejected\"}"));
                    })
                    .Then(_ => Task.FromResult(Respond(HttpStatusCode.Created, new JObject
                    {
                        ["status"] = "complete",
                        ["payment_preimage"] = new string('2', 64),
                        ["amount_msat"] = 250_000_000,
                        ["amount_sent_msat"] = 250_001_000
                    }.ToString())));
                var client = CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"))
                {
                    RuneFilePath = runeFile
                });
                var response = await ((ILightningClient)client).Pay(AmountInvoice);

                Assert.Equal(PayResult.Ok, response.Result);
                Assert.Equal(new[] { Rune, RotatedRune }, clnrest.Requests.Select(r => r.Rune));
                Assert.All(clnrest.Requests, r => Assert.Equal("xpay", r.Uri.Segments.Last()));
                Assert.Equal(clnrest.Requests[0].Body, clnrest.Requests[1].Body);
            }
            finally
            {
                File.Delete(runeFile);
            }
        }

        public static IEnumerable<object[]> InvalidRunes()
        {
            foreach (var rune in new[]
            {
                "", "not-a-rune", "abc", "A", Rune + "=", Rune.Insert(3, "="),
                Rune.Replace('-', '+'), RotatedRune.Replace('_', '/'), Rune.Insert(4, "\r\n"),
                Rune.Insert(4, " "), Rune.Insert(4, "\u00e9"),
                Convert.ToBase64String(new byte[31]),
                EncodeRune("=0\0&method=getinfo"),
                new string('A', ClnRestTransport.MaxRuneSize + 4)
            })
                yield return new object[] { rune };
        }

        [Theory]
        [MemberData(nameof(InvalidRunes))]
        public void RejectsInvalidInlineRunesWhenCreatingRestClient(string rune)
        {
            var client = new CLightningConnectionStringHandler().Create(
                $"type=clightning;server=http://127.0.0.1:3010/;rune={rune}", Network.Main, out var error);

            Assert.Null(client);
            Assert.Equal("The key 'rune' has invalid format", error);
        }

        [Theory]
        [MemberData(nameof(InvalidRunes))]
        public async Task InvalidRunesAreRejectedBeforeSending(string rune)
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
            try
            {
                File.WriteAllText(runeFile, rune);
                foreach (var fromFile in new[] { false, true })
                {
                    var clnrest = new FakeClnRest();
                    var settings = new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"));
                    if (fromFile)
                        settings.RuneFilePath = runeFile;
                    else
                        settings.Rune = rune;
                    var client = CreateClient(clnrest, settings);
                    // Invalid credentials are a local failure, not an unknown payment outcome.
                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                        () => ((ILightningClient)client).Pay(AmountInvoice));
                    Assert.Empty(clnrest.Requests);
                    if (rune.Length > 0)
                        Assert.DoesNotContain(rune, ex.Message);
                }
            }
            finally
            {
                File.Delete(runeFile);
            }
        }

        [Theory]
        [InlineData(Rune)]
        [InlineData(RotatedRune)]
        [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
        public async Task AcceptsPaddedAndUnpaddedRunes(string rune)
        {
            var clnrest = new FakeClnRest(Respond(HttpStatusCode.Created, "{}"), Respond(HttpStatusCode.Created, "{}"));
            await CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"))
            {
                Rune = rune
            }).GetInfoAsync();
            await CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"))
            {
                Rune = rune.TrimEnd('=')
            }).GetInfoAsync();

            Assert.Equal(new[] { rune, rune.TrimEnd('=') }, clnrest.Requests.Select(r => r.Rune));
        }

        [Fact]
        public async Task RuneFileContentHasAnEncodedSizeLimit()
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
            try
            {
                var restrictions = "=0&comment#" + new string('x', ClnRestTransport.MaxRuneSize / 4 * 3 - 32 - "=0&comment#".Length);
                var rune = EncodeRune(restrictions);
                Assert.Equal(ClnRestTransport.MaxRuneSize, rune.Length);
                File.WriteAllText(runeFile, rune + "\n");
                var clnrest = new FakeClnRest(Respond(HttpStatusCode.Created, "{}"));
                var client = CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"))
                {
                    RuneFilePath = runeFile
                });
                await client.GetInfoAsync();
                Assert.Single(clnrest.Requests);
                Assert.Equal(rune, clnrest.Requests[0].Rune);

                File.WriteAllText(runeFile, EncodeRune(restrictions + "xxx"));
                var newClient = CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/"))
                {
                    RuneFilePath = runeFile
                });
                await Assert.ThrowsAsync<InvalidOperationException>(() => newClient.GetInfoAsync());
                Assert.Single(clnrest.Requests);
            }
            finally
            {
                File.Delete(runeFile);
            }
        }

        static string EncodeRune(string restrictions)
        {
            var bytes = new byte[32 + Encoding.UTF8.GetByteCount(restrictions)];
            Encoding.UTF8.GetBytes(restrictions, 0, restrictions.Length, bytes, 32);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');
        }

        // Only the codes that xpay returns before it sends any part are immediately final.
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

        [Theory]
        [InlineData("failed", PayResult.Error)]
        [InlineData("pending", PayResult.Unknown)]
        [InlineData("complete", PayResult.Ok)]
        [InlineData("unrecognized", PayResult.Unknown)]
        [InlineData(null, PayResult.Unknown)]
        public async Task DestinationFailureReconcilesPayment(string status, PayResult expected)
        {
            var pays = new JObject
            {
                ["pays"] = status == null ? new JArray() : new JArray(new JObject
                {
                    ["payment_hash"] = AmountInvoiceHash,
                    ["status"] = status,
                    ["amount_msat"] = 250_000_000,
                    ["amount_sent_msat"] = 250_001_000,
                    ["preimage"] = new string('2', 64)
                })
            };
            var clnrest = new FakeClnRest(
                Respond(HttpStatusCode.InternalServerError, "{\"code\":203,\"message\":\"Destination said it doesn't know invoice\"}"),
                Respond(HttpStatusCode.Created, pays.ToString()));

            var response = await ((ILightningClient)CreateClient(clnrest)).Pay(AmountInvoice);

            Assert.Equal(expected, response.Result);
            Assert.Equal(new[] { "xpay", "listpays" }, clnrest.Requests.Select(r => r.Uri.Segments.Last()));
            Assert.Equal(AmountInvoiceHash, JObject.Parse(clnrest.Requests[1].Body)["payment_hash"].Value<string>());
            if (expected == PayResult.Error)
                Assert.Contains("doesn't know invoice", response.ErrorDetail);
        }

        [Theory]
        [InlineData("pending", PayResult.Unknown)]
        [InlineData("failed", PayResult.Error)]
        [InlineData("complete", PayResult.Ok)]
        public async Task DestinationFailureConsidersAllRecords(string status, PayResult expected)
        {
            var pays = new JObject
            {
                ["pays"] = new JArray(
                    new JObject { ["status"] = "failed" },
                    new JObject
                    {
                        ["payment_hash"] = AmountInvoiceHash,
                        ["status"] = status,
                        ["amount_msat"] = 250_000_000,
                        ["amount_sent_msat"] = 250_001_000,
                        ["preimage"] = new string('2', 64)
                    })
            };
            var clnrest = new FakeClnRest(
                Respond(HttpStatusCode.InternalServerError, "{\"code\":203,\"message\":\"Destination failed\"}"),
                Respond(HttpStatusCode.Created, pays.ToString()));

            var response = await ((ILightningClient)CreateClient(clnrest)).Pay(AmountInvoice);

            Assert.Equal(expected, response.Result);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError, "{\"code\":-1,\"message\":\"Lookup failed\"}")]
        [InlineData(HttpStatusCode.Created, "{\"pays\":{}}")]
        [InlineData(HttpStatusCode.Created, "{\"pays\":[{\"status\":\"complete\",\"payment_hash\":\"zz\"}]}")]
        public async Task DestinationFailureIsUnknownWhenLookupFails(HttpStatusCode status, string body)
        {
            var clnrest = new FakeClnRest(
                Respond(HttpStatusCode.InternalServerError, "{\"code\":203,\"message\":\"Destination failed\"}"),
                Respond(status, body));

            var response = await ((ILightningClient)CreateClient(clnrest)).Pay(AmountInvoice);

            Assert.Equal(PayResult.Unknown, response.Result);
            Assert.Equal(2, clnrest.Requests.Count);
        }

        [Fact]
        public async Task DestinationFailureLookupHonorsCallerCancellation()
        {
            var clnrest = new FakeClnRest(
                Respond(HttpStatusCode.InternalServerError, "{\"code\":203,\"message\":\"Destination failed\"}")).Then(Hang);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ((ILightningClient)CreateClient(clnrest)).Pay(AmountInvoice, cts.Token));
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
                RuneFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.rune")
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
            var settings = new CLightningRestSettings(new Uri("http://127.0.0.1:3010/")) { Rune = Rune };
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
            var client = new CLightningClient(new CLightningRestSettings(new Uri("http://127.0.0.1:3010/")) { Rune = Rune }, Network.Main, httpClient);

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
            var settings = new CLightningRestSettings(new Uri("http://127.0.0.1:3010/")) { Rune = Rune };
            using var listener = await ((ILightningClient)new CLightningClient(settings, Network.Main, httpClient)).Listen();

            await listener.WaitInvoice(CancellationToken.None);

            Assert.Equal(expected, JObject.Parse(clnrest.Requests[1].Body)["timeout"].Value<int>());
        }

        [Fact]
        public void RejectsAnHttpClientTimeoutUnderTwoSeconds()
        {
            var settings = new CLightningRestSettings(new Uri("http://127.0.0.1:3010/")) { Rune = Rune };
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
        public async Task ListenerKeepsUsingTheCachedRuneWhenTheFileIsRemoved()
        {
            var runeFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.rune");
            try
            {
                File.WriteAllText(runeFile, Rune);
                var clnrest = new FakeClnRest(
                    Respond(HttpStatusCode.Created, "{\"invoices\":[]}"),
                    Respond(HttpStatusCode.Created, PaidInvoice("a", 1).ToString()));
                var client = CreateClient(clnrest, new CLightningRestSettings(new Uri("http://127.0.0.1:3010/")) { RuneFilePath = runeFile });
                using var listener = await ((ILightningClient)client).Listen();

                File.Delete(runeFile);
                Assert.Equal("a", (await listener.WaitInvoice(CancellationToken.None)).Id);
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
        public async Task CallerConfiguredCertificateValidationIsHonored()
        {
            using var server = new TlsServer();
            var thumbprint = SHA256.HashData(server.Certificate.RawData);
            using var httpClient = CreatePinnedHttpClient(thumbprint);
            var settings = new CLightningRestSettings(server.Uri) { Rune = Rune };
            var info = await new CLightningClient(settings, Network.Main, httpClient).GetInfoAsync();

            Assert.NotNull(info);
            var wrong = RandomNumberGenerator.GetBytes(32);
            using var wrongClient = CreatePinnedHttpClient(wrong);
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => new CLightningClient(settings, Network.Main, wrongClient).GetInfoAsync());
            Assert.IsType<AuthenticationException>(ex.InnerException);
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

        [Theory]
        [InlineData("")]
        [InlineData(";allowinsecure=true")]
        [InlineData(";allowinsecure=false")]
        [InlineData(";allowinsecure=invalid")]
        [InlineData(";certthumbprint=c51bb1d402306d0da00e85581b32aa56166bcbab7eb888ff925d7167eb436d06")]
        [InlineData(";certthumbprint=invalid")]
        [InlineData(";certfilepath=/nonexistent/server.pem")]
        [InlineData(";certthumbprint=invalid;certfilepath=/nonexistent/server.pem;allowinsecure=invalid")]
        public async Task LegacyTlsOptionsAlwaysHonorInjectedHttpClient(string options)
        {
            foreach (var scheme in new[] { "http", "https" })
            {
                var clnrest = new FakeClnRest(Respond(HttpStatusCode.Created, "{}"));
                using var httpClient = new HttpClient(clnrest);
                var connectionString = $"type=clightning;server={scheme}://127.0.0.1:3010/;rune={Rune}";
                var client = Assert.IsType<CLightningClient>(new CLightningConnectionStringHandler(httpClient).Create(
                    connectionString + options, Network.Main, out var error));

                Assert.Null(error);
                await client.GetInfoAsync();
                Assert.Single(clnrest.Requests);
                Assert.Equal(Rune, clnrest.Requests[0].Rune);
                Assert.Equal($"{scheme}://127.0.0.1:3010/v1/getinfo", clnrest.Requests[0].Uri.AbsoluteUri);
            }
        }

        [Fact]
        public async Task DefaultHttpsRejectsUntrustedCertificateEvenWithLegacyOptions()
        {
            using var server = new TlsServer();
            var client = Assert.IsType<CLightningClient>(new CLightningConnectionStringHandler().Create(
                $"type=clightning;server={server.Uri};rune={Rune};allowinsecure=true;certfilepath=/nonexistent/server.pem;certthumbprint=invalid",
                Network.Main, out var error));

            Assert.Null(error);
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetInfoAsync());
        }

        [Theory]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/", "The key 'rune' or 'runefilepath' is mandatory for clnrest")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/clnrest;rune=abc", "The key 'server' should be the root URL of clnrest, without a path, query or fragment")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/?;rune=abc", "The key 'server' should be the root URL of clnrest, without a path, query or fragment")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/#;rune=abc", "The key 'server' should be the root URL of clnrest, without a path, query or fragment")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010?a=b;rune=abc", "The key 'server' should be the root URL of clnrest, without a path, query or fragment")]
        [InlineData("type=clightning;server=https://internal:8443/admin/rotate-keys?;rune=abc", "The key 'server' should be the root URL of clnrest, without a path, query or fragment")]
        [InlineData("type=clightning;server=https://127.0.0.1:3010/;rune=abc;runefilepath=/tmp/a.rune", "The key 'rune' is already specified")]
        [InlineData("type=clightning;server=tcp://127.0.0.1:9835/;rune=abc", "The keys 'rune' and 'runefilepath' need a clnrest server starting by http:// or https://")]
        [InlineData("type=clightning;server=unix://root/.lightning/lightning-rpc;runefilepath=/tmp/a.rune", "The keys 'rune' and 'runefilepath' need a clnrest server starting by http:// or https://")]
        public void RejectsInvalidClnRestConnectionStrings(string connectionString, string expectedError)
        {
            var client = new CLightningConnectionStringHandler().Create(connectionString, Network.RegTest, out var error);

            Assert.Null(client);
            Assert.Equal(expectedError, error);
        }

        static CLightningClient CreateClient(FakeClnRest clnrest, CLightningRestSettings settings = null)
        {
            settings ??= new CLightningRestSettings(new Uri("http://127.0.0.1:3010/")) { Rune = Rune };
            return new CLightningClient(settings, Network.Main, new HttpClient(clnrest));
        }

        static HttpClient CreatePinnedHttpClient(byte[] thumbprint)
        {
            return new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                    certificate != null && CryptographicOperations.FixedTimeEquals(thumbprint, SHA256.HashData(certificate.RawData))
            });
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
