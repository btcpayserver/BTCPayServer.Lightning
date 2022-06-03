using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Charge;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Lightning.Eclair;
using BTCPayServer.Lightning.LND;
using BTCPayServer.Lightning.LndHub;
using NBitcoin.Crypto;
using NBitcoin.RPC;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Lightning.Tests
{
    public class CommonTests
    {
#if DEBUG
        public const int Timeout = 20 * 60 * 1000;
#else
		public const int Timeout = 2 * 60 * 1000;
#endif
        public CommonTests(ITestOutputHelper helper)
        {
            Docker = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IN_DOCKER_CONTAINER"));
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
            ConnectChannels.Logs = Logs.LogProvider.CreateLogger("Tests");
        }

        public static bool Docker { get; set; }

        [Fact(Timeout = Timeout)]
        public async Task CanCreateInvoice()
        {
            const int amount = 10001;
            await WaitServersAreUp();
            foreach (var client in Tester.GetLightningClients())
            {
                Logs.Tester.LogInformation($"{client.Name}: {nameof(CanCreateInvoice)}");
                var expectedAmount = client.Client is LndHubLightningClient 
                    ? LightMoney.Satoshis(amount/1000) // LNDhub accounts with sats instead of msat
                    : LightMoney.MilliSatoshis(amount);
                var createdInvoice = await client.Client.CreateInvoice(amount, "CanCreateInvoice", TimeSpan.FromMinutes(5));
                var retrievedInvoice = await client.Client.GetInvoice(createdInvoice.Id);
                
                AssertUnpaid(createdInvoice, expectedAmount);
                Assert.True(createdInvoice.ExpiresAt > DateTimeOffset.UtcNow);
                AssertUnpaid(retrievedInvoice, expectedAmount);
                Assert.True(retrievedInvoice.ExpiresAt > DateTimeOffset.UtcNow);
                retrievedInvoice = await client.Client.GetInvoice("c4180c13ae6b43e261c4c6f43c1b6760cfc80ba5a06643f383ece30d7316e4a6");
                Assert.Null(retrievedInvoice);
                retrievedInvoice = await client.Client.GetInvoice("lol");
                Assert.Null(retrievedInvoice);
            }
        }

        [Fact(Timeout = Timeout)]
        public async Task CanCreateInvoiceWithDescriptionHash()
        {
            var hashToUse =
                new uint256(new SHA256Managed().ComputeHash(Encoding.UTF8.GetBytes("CanCreateInvoiceWithDescriptionHash")));

            async Task<LightningInvoice> CreateWithHash(ILightningClient lightningClient)
            {
                return await lightningClient.CreateInvoice(new CreateInvoiceParams(10000, hashToUse,
                    TimeSpan.FromMinutes(5)));
            }
            await WaitServersAreUp();
            foreach (var client in Tester.GetLightningClients())
            {
                switch (client.Client)
                {
                    case CLightningClient _:
                    case LndClient _:
                        Logs.Tester.LogInformation($"{client.Name}: {nameof(CanCreateInvoiceWithDescriptionHash)}");
                        var createdInvoice = await CreateWithHash(client.Client);
                        var retrievedInvoice = await client.Client.GetInvoice(createdInvoice.Id);
                        Logs.Tester.LogInformation(JObject.FromObject(createdInvoice).ToString());
                        Logs.Tester.LogInformation(JObject.FromObject(retrievedInvoice).ToString());
                        AssertUnpaid(createdInvoice);
                        AssertUnpaid(retrievedInvoice);
                        var createdInvoiceBOLT = BOLT11PaymentRequest.Parse(createdInvoice.BOLT11, Network.RegTest);
                        var retrievedInvoiceBOLT = BOLT11PaymentRequest.Parse(retrievedInvoice.BOLT11, Network.RegTest);
                        Assert.Equal(createdInvoiceBOLT.PaymentHash, retrievedInvoiceBOLT.PaymentHash);
                        Assert.Equal(hashToUse, createdInvoiceBOLT.DescriptionHash);
                        break;
                    case LndHubLightningClient _:
                        // Once this gets merged, we can support it too: https://github.com/BlueWallet/LndHub/pull/319
                        // Skip and don't throw the exception, because the compatible lndhub.Go by Alby supports it
                        break;
                    default:
                        await Assert.ThrowsAsync<NotSupportedException>(async () =>
                        {
                            await CreateWithHash(client.Client);
                        });
                        break;
                }
            }
        }

        [Fact(Timeout = Timeout)]
        public async Task CanCancelInvoices()
        {
            async Task CreateAndCancel(ILightningClient lightningClient)
            {
                var i = await lightningClient.CreateInvoice(new LightMoney(10000), "hi there", TimeSpan.FromMinutes(10),
                    CancellationToken.None);

                await lightningClient.CancelInvoice(i.Id);
                i = await lightningClient.GetInvoice(i.Id);
                Assert.Null(i);
            }
            
            await WaitServersAreUp();
            
            foreach (var client in Tester.GetLightningClients())
            {
                switch (client.Client)
                {
                    case ChargeClient _:
                    case CLightningClient _:
                    case LndClient _:
                        Logs.Tester.LogInformation($"{client.Name}: {nameof(CanCancelInvoices)}");
                        await CreateAndCancel(client.Client);
                        break;
                    default:
                        await Assert.ThrowsAsync<NotSupportedException>(async () =>
                        {
                            await CreateAndCancel(client.Client);
                        });
                        break;
                }
            }
        }

        [Fact]
        public async Task CanCreateInvoiceUsingConnectionString()
        {
            var network = Tester.Network;
            ILightningClientFactory factory = new LightningClientFactory(network);
                
            var connectionStrings = Docker
                ? new List<string>
                {
                    "type=charge;server=http://api-token:foiewnccewuify@charge:9112;allowinsecure=true",
                    "type=lnd-rest;server=http://lnd_dest:8080;allowinsecure=true",
                    "type=clightning;server=tcp://lightningd:9835",
                    "type=eclair;server=http://eclair:8080;password=bukkake"
                }
                : new List<string>
                {
                    "type=charge;server=http://api-token:foiewnccewuify@127.0.0.1:37462;allowinsecure=true",
                    "type=lnd-rest;server=http://127.0.0.1:42802;allowinsecure=true",
                    "type=clightning;server=tcp://127.0.0.1:48532",
                    "type=eclair;server=http://127.0.0.1:4570;password=bukkake"
                };
            
            // LNDhub needs an account first
            var lndhubServer = Docker ? "http://lndhub:3000" : "http://127.0.0.1:42923";
            var lndhubClient = new LndHubLightningClient(new Uri(lndhubServer), "login", "password", network);
            var data = await lndhubClient.CreateAccount();
            lndhubServer = lndhubServer.Replace("://", $"://{data.Login}:{data.Password}@");
            connectionStrings.Add($"type=lndhub;server={lndhubServer};allowinsecure=true");

            var clientTypes = Tester.GetLightningClients().Select(l => l.Client.GetType()).ToArray();
            foreach (var connectionString in connectionStrings)
            {
                var client = factory.Create(connectionString);
                if (!clientTypes.Contains(client.GetType())) continue;
                
                var createdInvoice = await client.CreateInvoice(10000, "CanCreateInvoiceUsingConnectionString", TimeSpan.FromMinutes(5));
                var retrievedInvoice = await client.GetInvoice(createdInvoice.Id);
                AssertUnpaid(createdInvoice);
                AssertUnpaid(retrievedInvoice);
            }
        }

        [Fact(Timeout = Timeout)]
        public async Task CanGetInfo()
        {
            await WaitServersAreUp();
            foreach (var client in Tester.GetLightningClients())
            {
                Logs.Tester.LogInformation($"{client.Name}: {nameof(CanGetInfo)}");
                var info = await client.Client.GetInfo();
                Assert.NotNull(info);
                Assert.True(info.BlockHeight > 0);
                Assert.NotEmpty(info.NodeInfoList);
            }
        }

        [Fact(Timeout = Timeout)]
        public async Task CanGetBalance()
        {
            // for channel values to be in reasonable bounds
            var lowerBound = LightMoney.Zero;
            var upperBound = LightMoney.Satoshis(16777215);
            
            await WaitServersAreUp();
            foreach (var test in Tester.GetTestedPairs())
            {
                await EnsureConnectedToDestinations(test);
                var client = test.Customer;
                Logs.Tester.LogInformation($"{test.Name}: {nameof(CanGetBalance)}");
                switch (client)
                {
                    case LndClient _:
                    case CLightningClient _:
                    case EclairLightningClient _:
                    case LndHubLightningClient _:
                        var balance = await client.GetBalance();
                        Assert.NotNull(balance.OnchainBalance);
                        if (!(client is LndHubLightningClient))
                            Assert.True(balance.OnchainBalance.Confirmed > upperBound);
                        Assert.Equal(LightMoney.Zero,balance.OnchainBalance.Unconfirmed);
                        Assert.Equal(LightMoney.Zero,balance.OnchainBalance.Reserved);
                        Assert.NotNull(balance.OffchainBalance);
                        Assert.Equal(LightMoney.Zero, balance.OffchainBalance.Opening);
                        Assert.InRange(balance.OffchainBalance.Local, lowerBound, upperBound);
                        Assert.InRange(balance.OffchainBalance.Remote, lowerBound, upperBound);
                        Assert.Equal(LightMoney.Zero, balance.OffchainBalance.Closing);
                        break;

                    default:
                        await Assert.ThrowsAsync<NotSupportedException>(async () =>
                        {
                            await client.GetBalance();
                        });
                        break;
                }
            }
        }

        [Fact(Timeout = Timeout)]
        public async Task CanHandleSelfPayment()
        {
            await WaitServersAreUp();
            foreach (var client in Tester.GetLightningClients())
            {
                Logs.Tester.LogInformation($"{client.Name}: {nameof(CanHandleSelfPayment)}");
                var expiry = TimeSpan.FromSeconds(5000);
                var amount = LightMoney.Satoshis(21);
                var invoice = await client.Client.CreateInvoice(amount, "CanHandleSelfPayment", expiry);

                switch (client.Client)
                {
                    case LndClient _:
                    case CLightningClient _:
                    case EclairLightningClient _:
                        var response = await client.Client.Pay(invoice.BOLT11);
                        Assert.Equal(PayResult.CouldNotFindRoute, response.Result);
                        break;

                    case LndHubLightningClient _:
                        // The senders LNDhub wallet needs some initial funds.
                        await FundLndHubWallet(client.Client, amount + 10);
                        // LNDhub handles self-payment internally
                        var res = await client.Client.Pay(invoice.BOLT11);
                        Assert.Equal(PayResult.Ok, res.Result);
                        Assert.Equal(amount, res.Details.TotalAmount);
                        Assert.Equal(0, res.Details.FeeAmount);
                        break;

                    default:
                        await Assert.ThrowsAsync<NotSupportedException>(async () =>
                        {
                            await client.Client.Pay(invoice.BOLT11);
                        });
                        break;
                }
            }
        }

        private async Task WaitServersAreUp()
        {
            var clients = Tester.GetLightningClients().Select(c => WaitServersAreUp(c.Name, c.Client)).ToArray();
            await Task.WhenAll(clients);
        }

        private async Task WaitServersAreUp(string name, ILightningClient client)
        {
            var rpc = await GetRPCClient();
            await rpc.GenerateAsync(1);
            Exception realException = null;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Timeout - 5)))
            {
retry:
                try
                {
                    if (realException != null)
                        await Task.Delay(1000, cts.Token);
                    await client.GetInfo(cts.Token);
                    Logs.Tester.LogInformation($"{name}: Server is up");
                }
                catch (Exception ex) when (!cts.IsCancellationRequested)
                {
                    realException = ex;
                    goto retry;
                }
                catch (Exception)
                {
                    if (realException != null)
                        Logs.Tester.LogInformation(realException.ToString());
                    Assert.False(true, $"{name}: The server could not be started");
                }
            }
        }

        [Fact(Timeout = Timeout)]
        public async Task CanPayInvoiceAndReceive()
        {
            foreach (var test in Tester.GetTestedPairs())
            {
                Logs.Tester.LogInformation($"{test.Name}: {nameof(CanPayInvoiceAndReceive)}");
                await EnsureConnectedToDestinations(test);
                
                var expiry = TimeSpan.FromSeconds(5000);
                var amount = LightMoney.Satoshis(21);
                var invoice = await test.Merchant.CreateInvoice(amount, "CanPayInvoiceAndReceive", expiry);

                if (test.Customer is LndHubLightningClient)
                {
                    // The senders LNDhub wallet needs some initial funds.
                    await FundLndHubWallet(test.Customer, LightMoney.Satoshis(2100));
                }
                
                using var listener = await test.Merchant.Listen();
                var waiting = listener.WaitInvoice(default);
                var paidReply = await test.Customer.Pay(invoice.BOLT11);
                var paidInvoice = await GetPaidInvoice(listener, waiting, invoice.Id);

                Assert.Equal(PayResult.Ok, paidReply.Result);
                Assert.Equal(amount, paidReply.Details.TotalAmount);
                Assert.Equal(0, paidReply.Details.FeeAmount);

                Assert.Equal(LightningInvoiceStatus.Paid, paidInvoice.Status);
                Assert.Equal(amount, paidInvoice.Amount);
                Assert.Equal(amount, paidInvoice.AmountReceived);

                // check payment
                var payReq = BOLT11PaymentRequest.Parse(invoice.BOLT11, Network.RegTest);
                var hash = payReq.PaymentHash?.ToString();
                var payment = await test.Customer.GetPayment(hash);
                Assert.Equal(hash, payment.PaymentHash);
                Assert.Equal(amount, payment.Amount);
                Assert.Equal(amount, payment.AmountSent);
                Assert.Equal(0, payment.Fee);
                Assert.Equal(LightningPaymentStatus.Complete, payment.Status);

                // with max fee percent
                var invoiceMaxFeePercent = await test.Merchant.CreateInvoice(amount, "CanPayInvoiceWithMaxFeeAndReceivePercent", expiry);
                paidReply = await test.Customer.Pay(invoiceMaxFeePercent.BOLT11, new PayInvoiceParams { MaxFeePercent = 6.15f });
                Assert.Equal(PayResult.Ok, paidReply.Result);
                Assert.Equal(amount, paidReply.Details.TotalAmount);
                Assert.Equal(0, paidReply.Details.FeeAmount);

                // with fee below 1%
                var invoiceMaxFeePercent2 = await test.Merchant.CreateInvoice(amount, "CanPayInvoiceWithMaxFeeAndReceivePercent2", expiry);
                paidReply = await test.Customer.Pay(invoiceMaxFeePercent2.BOLT11, new PayInvoiceParams { MaxFeePercent = 0.5f });
                Assert.Equal(PayResult.Ok, paidReply.Result);
                Assert.Equal(amount, paidReply.Details.TotalAmount);
                Assert.Equal(0, paidReply.Details.FeeAmount);

                // with max fee limit
                var invoiceMaxFeeLimit = await test.Merchant.CreateInvoice(amount, "CanPayInvoiceWithMaxFeeAndReceiveLimit", expiry);
                paidReply = await test.Customer.Pay(invoiceMaxFeeLimit.BOLT11, new PayInvoiceParams { MaxFeeFlat = Money.Satoshis(100) });
                Assert.Equal(PayResult.Ok, paidReply.Result);
                Assert.Equal(amount, paidReply.Details.TotalAmount);
                Assert.Equal(0, paidReply.Details.FeeAmount);

                // with zero/explicit amount
                if (test.Customer is LndHubLightningClient)
                {
                    await Assert.ThrowsAsync<LndHubClient.LndHubApiException>(async () =>
                    {
                        await test.Merchant.CreateInvoice(LightMoney.Zero, "CanPayInvoiceWithZeroAmount", expiry);
                    });
                }
                else
                {
                    var invoiceZeroAmount = await test.Merchant.CreateInvoice(LightMoney.Zero, "CanPayInvoiceWithZeroAmount", expiry);
                    paidReply = await test.Customer.Pay(invoiceZeroAmount.BOLT11, new PayInvoiceParams { Amount = amount });
                    Assert.Equal(PayResult.Ok, paidReply.Result);
                    Assert.Equal(amount, paidReply.Details.TotalAmount);
                    Assert.Equal(0, paidReply.Details.FeeAmount);
                }
            }
        }

        [Fact(Timeout = Timeout)]
        public async Task CanSendCorrectConnectError()
        {
            foreach (var test in Tester.GetTestedPairs())
            {
                await EnsureConnectedToDestinations(test);
                var src = test.Customer;
                var dest = test.Merchant;

                var info = await dest.GetInfo();
                var node = info.NodeInfoList.First();
                
                switch (src)
                {
                    case LndHubLightningClient _:
                        await Assert.ThrowsAsync<NotSupportedException>(async () =>
                        {
                            await src.ConnectTo(node);
                        });
                        break;

                    default:
                        // Reconnecting to same node should be no op
                        Assert.Equal(ConnectionResult.Ok, await src.ConnectTo(node));
                        Assert.Equal(ConnectionResult.CouldNotConnect, await src.ConnectTo(new NodeInfo(new Key().PubKey, "127.0.0.2", node.Port)));
                        Assert.Equal(ConnectionResult.CouldNotConnect, await src.ConnectTo(new NodeInfo(new Key().PubKey, node.Host, node.Port)));
                        break;
                }


            }
        }

        [Fact(Timeout = Timeout)]
        public async Task CanGetDepositAddress()
        {
            await WaitServersAreUp();
            foreach (var client in Tester.GetLightningClients())
            {
                Logs.Tester.LogInformation($"{client.Name}: {nameof(CanGetDepositAddress)}");
                try
                {
                    var address = await client.Client.GetDepositAddress();
                    Assert.NotNull(address);
                }
                catch (NotSupportedException)
                {
                    Logs.Tester.LogInformation($"{client.Name}: {nameof(CanGetDepositAddress)} not supported");
                }
            }
        }

        [Fact(Timeout = Timeout)]
        public async Task CanWaitListenInvoice()
        {
            foreach (var test in Tester.GetTestedPairs())
            {
                await EnsureConnectedToDestinations(test);

                var amount1 = 6150;
                var amount2 = 8251;
                var src = test.Customer;
                var dest = test.Merchant;
                Logs.Tester.LogInformation($"{test.Name}: {nameof(CanWaitListenInvoice)}");
                var merchantInvoice1 = await dest.CreateInvoice(amount1, "Hello world", TimeSpan.FromSeconds(3600));
                Logs.Tester.LogInformation($"{test.Name}: Created invoice {merchantInvoice1.Id}");
                var merchantInvoice2 = await dest.CreateInvoice(amount2, "Hello world", TimeSpan.FromSeconds(3600));
                Logs.Tester.LogInformation($"{test.Name}: Created invoice {merchantInvoice2.Id}");
                var waitToken = default(CancellationToken);
                var listener = await dest.Listen(waitToken);
                var waitTask = listener.WaitInvoice(waitToken);

                if (src is LndHubLightningClient)
                {
                    // Change amounts to whole sats for comparison - LNDhub only returns sats
                    amount1 = 6000;
                    amount2 = 8000;
                    // The senders LNDhub wallet needs some initial funds.
                    await FundLndHubWallet(src, LightMoney.Satoshis(2100));
                }
                
                var payResponse = await src.Pay(merchantInvoice1.BOLT11, waitToken);
                Assert.Equal(PayResult.Ok, payResponse.Result);
                AssertEqual(amount1, payResponse.Details.TotalAmount);
                Logs.Tester.LogInformation($"{test.Name}: Paid invoice {merchantInvoice1.Id}");

                var invoice = await waitTask;
                Logs.Tester.LogInformation($"{test.Name}: Notification received for {invoice.Id}");
                Assert.Equal(invoice.Id, merchantInvoice1.Id);
                Assert.True(invoice.PaidAt.HasValue);
                AssertEqual(amount1, invoice.AmountReceived);

                var waitTask2 = listener.WaitInvoice(waitToken);

                payResponse = await src.Pay(merchantInvoice2.BOLT11, waitToken);
                AssertEqual(amount2, payResponse.Details.TotalAmount);
                Logs.Tester.LogInformation($"{test.Name}: Paid invoice {merchantInvoice2.Id}");
                invoice = await waitTask2;
                Logs.Tester.LogInformation($"{test.Name}: Notification received for {invoice.Id}");
                Assert.True(invoice.PaidAt.HasValue);

                AssertEqual(invoice.Amount, invoice.AmountReceived);
                Assert.Equal(invoice.Id, merchantInvoice2.Id);
                AssertEqual(amount2, invoice.AmountReceived);
                var waitTask3 = listener.WaitInvoice(waitToken);
                await Task.Delay(100, waitToken);
                listener.Dispose();
                Logs.Tester.LogInformation($"{test.Name}: Listener disposed, should throw exception");
                Assert.Throws<OperationCanceledException>(() => waitTask3.GetAwaiter().GetResult());
            }
        }

        private static void AssertEqual(LightMoney a, LightMoney b)
        {
            Assert.Equal(a.ToDecimal(LightMoneyUnit.Satoshi), b.ToDecimal(LightMoneyUnit.Satoshi), 2);
        }

        [Fact]
        public void LightMoneyOverflowTest()
        {
            var maxSupply = 21_000_000m;
            var v = LightMoney.Coins(maxSupply);
            Assert.Equal(v, LightMoney.Satoshis(maxSupply * (decimal)Math.Pow(10, 8)));
            Assert.Equal(v, LightMoney.Bits(maxSupply * (decimal)Math.Pow(10, 6)));
            Assert.Equal(v, LightMoney.MilliSatoshis((long)(maxSupply * (decimal)Math.Pow(10, 11))));
        }

        [Fact(Timeout = Timeout)]
        public async Task DoNotReportOkIfChannelCantConnect()
        {
            foreach (var client in Tester.GetTestedPairs())
            {
                Logs.Tester.LogInformation(client.Customer.GetType().Name);
                
                switch (client.Customer)
                {
                    case LndHubLightningClient _:
                        await Assert.ThrowsAsync<NotSupportedException>(async () =>
                        {
                            await client.Customer.ConnectTo(new NodeInfo(new Key().PubKey, "127.0.0.1", 99_999));
                        });
                        break;

                    default:
                        var result = await client.Customer.ConnectTo(new NodeInfo(new Key().PubKey, "127.0.0.1", 99_999));
                        Assert.Equal(ConnectionResult.CouldNotConnect, result);

                        var ni = (await client.Merchant.GetInfo()).NodeInfoList.FirstOrDefault();
                        Assert.Equal(ConnectionResult.Ok, await client.Customer.ConnectTo(ni));
                        break;
                }
            }
        }

        [Fact(Timeout = Timeout)]
        public async Task CanListChannels()
        {
            // for channel values to be in reasonable bounds
            var lowerBound = LightMoney.Satoshis(10000000);
            var upperBound = LightMoney.Satoshis(16777215);

            foreach (var test in Tester.GetTestedPairs())
            {
                switch (test.Customer)
                {
                    case LndHubLightningClient _:
                        await Assert.ThrowsAsync<NotSupportedException>(async () =>
                        {
                            await test.Customer.ListChannels();
                        });
                        break;

                    default:
                        await EnsureConnectedToDestinations(test);

                        var senderChannels = await test.Customer.ListChannels();
                        var senderInfo = await test.Customer.GetInfo();
                        Assert.NotEmpty(senderChannels);
                        Assert.Single(senderChannels.Where(s => s.IsActive));

                        var destChannels = await test.Merchant.ListChannels();
                        var destInfo = await test.Merchant.GetInfo();
                        Assert.NotEmpty(destChannels);
                        Assert.Single(destChannels.GroupBy(s => s.RemoteNode));
                        foreach (var c in senderChannels)
                        {
                            Assert.NotNull(c.RemoteNode);
                            Assert.True(c.IsPublic);
                            Assert.True(c.IsActive);
                            Assert.NotNull(c.ChannelPoint);
                            Assert.InRange(c.Capacity, lowerBound, upperBound);
                            Assert.InRange(c.LocalBalance, lowerBound, upperBound);
                        }
                        Assert.Contains(senderChannels, c => c.RemoteNode.Equals(destInfo.NodeInfoList.FirstOrDefault()?.NodeId));
                        Assert.Contains(destChannels, c => c.RemoteNode.Equals(senderInfo.NodeInfoList.FirstOrDefault()?.NodeId));
                        break;
                }
            }
        }

        [Fact]
        public void CanParseBOLT11()
        {
            var key = new Key(Encoders.Hex.DecodeData("e126f68f7eafcc8b74f54d269fe206be715000f94dac067d1c04a8ca3b2db734"));

            var p = BOLT11PaymentRequest.Parse("lnbc1pvjluezpp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqdpl2pkx2ctnv5sxxmmwwd5kgetjypeh2ursdae8g6twvus8g6rfwvs8qun0dfjkxaq8rkx3yf5tcsyz3d73gafnh3cax9rn449d9p5uxz9ezhhypd0elx87sjle52x86fux2ypatgddc6k63n7erqz25le42c4u4ecky03ylcqca784w", Network.Main);
            Assert.Equal("lnbc", p.Prefix);
            Assert.Equal(LightMoney.Zero, p.MinimumAmount);
            Assert.Equal(1496314658UL, Utils.DateTimeToUnixTime(p.Timestamp));
            Assert.Equal(1496314658UL + 60 * 60, Utils.DateTimeToUnixTime(p.ExpiryDate));
            Assert.Equal("0001020304050607080900010203040506070809000102030405060708090102", p.PaymentHash.ToString());
            Assert.Equal("Please consider supporting this project", p.ShortDescription);
            Assert.Null(p.PaymentSecret);
            Assert.True(p.FeatureBits.HasFlag(FeatureBits.None));

            var preimage = Encoders.Hex.DecodeData("6c6e62630b25fe64410d00004080c1014181c20240004080c1014181c20240004080c1014181c202404081a1fa83632b0b9b29031b7b739b4b232b91039bab83837b93a34b733903a3434b990383937b532b1ba0");
            var hash = new uint256(Encoders.Hex.DecodeData("c3d4e83f646fa79a393d75277b1d858db1d1f7ab7137dcb7835db2ecd518e1c9"));
            Assert.Equal(hash, new uint256(Hashes.SHA256(preimage)));
            Assert.Equal(hash, p.Hash);
            Assert.True(key.PubKey.Verify(hash, p.ECDSASignature));
            Assert.True(p.GetPayeePubKey().Verify(hash, p.ECDSASignature));
            Assert.True(p.VerifySignature());
            Assert.Equal(key.PubKey, p.GetPayeePubKey());

            p = BOLT11PaymentRequest.Parse("lnbcrt20u1psd66dppp5m4ughz9keyptj80qcn35cx9w52p7gc8eyx4m6y5456jlhm04wfvsdqqcqzpgxqyz5vqsp5pdsxhsnrs69n940373fnec2zxw5yzlksnev40ejcq39lnju5lt3s9qyyssqpq760qvf46y3cch948wau8e5ym0zungnqfvdx5wruy6f0hru2pp9txtc9up2lfc439a2xuz6nvgjw40vsddhywjpc5qmm0q3dj4m3dcqxzjjeg", Network.RegTest);
            Assert.Equal("lnbcrt", p.Prefix);
            Assert.Equal(40, p.MinFinalCLTVExpiry);
            Assert.Equal(LightMoney.FromUnit(2000m, LightMoneyUnit.Satoshi), p.MinimumAmount);
            Assert.Equal(LightMoney.FromUnit(2000m * 0.00000001m, LightMoneyUnit.BTC), p.MinimumAmount);
            Assert.Equal(1625123233UL, Utils.DateTimeToUnixTime(p.Timestamp));
            Assert.Equal(1625123233UL + 24 * 60 * 60, Utils.DateTimeToUnixTime(p.ExpiryDate));
            Assert.Equal("dd788b88b6c902b91de0c4e34c18aea283e460f921abbd1295a6a5fbedf57259", p.PaymentHash.ToString());
            Assert.Equal("", p.ShortDescription);
            Assert.Equal("0b606bc263868b32d5f1f4533ce14233a8417ed09e5957e658044bf9cb94fae3", p.PaymentSecret.ToString());
            Assert.True((p.FeatureBits | (FeatureBits.MPPOptional | FeatureBits.PaymentAddrRequired | FeatureBits.TLVOnionPayloadOptional)) ==
                (FeatureBits.MPPOptional | FeatureBits.PaymentAddrRequired | FeatureBits.TLVOnionPayloadOptional));
            Assert.True(p.VerifySignature());

            p = BOLT11PaymentRequest.Parse("lnbc2500u1pvjluezpp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqdq5xysxxatsyp3k7enxv4jsxqzpuaztrnwngzn3kdzw5hydlzf03qdgm2hdq27cqv3agm2awhz5se903vruatfhq77w3ls4evs3ch9zw97j25emudupq63nyw24cg27h2rspfj9srp", Network.Main);
            Assert.Equal("lnbc", p.Prefix);
            Assert.Equal(9, p.MinFinalCLTVExpiry);
            Assert.Equal(LightMoney.FromUnit(2500m, LightMoneyUnit.Micro), p.MinimumAmount);
            Assert.Equal(LightMoney.FromUnit(2500m * 0.000001m, LightMoneyUnit.BTC), p.MinimumAmount);
            Assert.Equal(1496314658UL, Utils.DateTimeToUnixTime(p.Timestamp));
            Assert.Equal(1496314658UL + 60, Utils.DateTimeToUnixTime(p.ExpiryDate));
            Assert.Equal("0001020304050607080900010203040506070809000102030405060708090102", p.PaymentHash.ToString());
            Assert.Equal("1 cup coffee", p.ShortDescription);
            Assert.True(p.VerifySignature());
            // Same but with uppercase and url prefix
            p = BOLT11PaymentRequest.Parse("lightniNG:LNBC2500U1PVJLUEZPP5QQQSYQCYQ5RQWZQFQQQSYQCYQ5RQWZQFQQQSYQCYQ5RQWZQFQYPQDQ5XYSXXATSYP3K7ENXV4JSXQZPUAZTRNWNGZN3KDZW5HYDLZF03QDGM2HDQ27CQV3AGM2AWHZ5SE903VRUATFHQ77W3LS4EVS3CH9ZW97J25EMUDUPQ63NYW24CG27H2RSPFJ9SRP".ToUpperInvariant(), Network.Main);
            Assert.Equal("lnbc", p.Prefix);
            Assert.Equal(LightMoney.FromUnit(2500m, LightMoneyUnit.Micro), p.MinimumAmount);
            Assert.Equal(LightMoney.FromUnit(2500m * 0.000001m, LightMoneyUnit.BTC), p.MinimumAmount);
            Assert.Equal(1496314658UL, Utils.DateTimeToUnixTime(p.Timestamp));
            Assert.Equal(1496314658UL + 60, Utils.DateTimeToUnixTime(p.ExpiryDate));
            Assert.Equal("0001020304050607080900010203040506070809000102030405060708090102", p.PaymentHash.ToString());
            Assert.Equal("1 cup coffee", p.ShortDescription);

            p = BOLT11PaymentRequest.Parse("lnbc2500u1pvjluezpp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqdpquwpc4curk03c9wlrswe78q4eyqc7d8d0xqzpuyk0sg5g70me25alkluzd2x62aysf2pyy8edtjeevuv4p2d5p76r4zkmneet7uvyakky2zr4cusd45tftc9c5fh0nnqpnl2jfll544esqchsrny", Network.Main);
            Assert.Equal("lnbc", p.Prefix);
            Assert.Equal(LightMoney.FromUnit(2500m, LightMoneyUnit.Micro), p.MinimumAmount);
            Assert.Equal(LightMoney.FromUnit(2500m * 0.000001m, LightMoneyUnit.BTC), p.MinimumAmount);
            Assert.Equal(1496314658UL, Utils.DateTimeToUnixTime(p.Timestamp));
            Assert.Equal(1496314658UL + 60, Utils.DateTimeToUnixTime(p.ExpiryDate));
            Assert.Equal("0001020304050607080900010203040506070809000102030405060708090102", p.PaymentHash.ToString());
            Assert.Equal("ナンセンス 1杯", p.ShortDescription);
            hash = new uint256(Encoders.Hex.DecodeData("197a3061f4f333d86669b8054592222b488f3c657a9d3e74f34f586fb3e7931c"));
            Assert.True(key.PubKey.Verify(hash, p.ECDSASignature));
            Assert.Equal(key.PubKey, p.GetPayeePubKey());

            p = BOLT11PaymentRequest.Parse("lnbc20m1pvjluezpp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqhp58yjmdan79s6qqdhdzgynm4zwqd5d7xmw5fk98klysy043l2ahrqscc6gd6ql3jrc5yzme8v4ntcewwz5cnw92tz0pc8qcuufvq7khhr8wpald05e92xw006sq94mg8v2ndf4sefvf9sygkshp5zfem29trqq2yxxz7", Network.Main);
            Assert.Equal("lnbc", p.Prefix);
            Assert.Equal(LightMoney.FromUnit(20m, LightMoneyUnit.MilliBTC), p.MinimumAmount);
            Assert.Equal(LightMoney.FromUnit(20m * 0.001m, LightMoneyUnit.BTC), p.MinimumAmount);
            Assert.Equal(1496314658UL, Utils.DateTimeToUnixTime(p.Timestamp));
            Assert.Equal(1496314658UL + 60 * 60, Utils.DateTimeToUnixTime(p.ExpiryDate));
            Assert.Equal("0001020304050607080900010203040506070809000102030405060708090102", p.PaymentHash.ToString());
            Assert.Null(p.ShortDescription);
            hash = new uint256(Encoders.Hex.DecodeData("b6025e8a10539dddbcbe6840a9650707ae3f147b8dcfda338561ada710508916"));
            Assert.Equal(hash, p.Hash);
            Assert.True(key.PubKey.Verify(hash, p.ECDSASignature));
            Assert.True(p.VerifySignature());
            Assert.Equal(key.PubKey, p.GetPayeePubKey());

            Assert.True(p.VerifyDescriptionHash("One piece of chocolate cake, one icecream cone, one pickle, one slice of swiss cheese, one slice of salami, one lollypop, one piece of cherry pie, one sausage, one cupcake, and one slice of watermelon"));
            Assert.False(p.VerifyDescriptionHash("lol"));

            p = BOLT11PaymentRequest.Parse("lnbc20m1pvjluezpp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqhp58yjmdan79s6qqdhdzgynm4zwqd5d7xmw5fk98klysy043l2ahrqscc6gd6ql3jrc5yzme8v4ntcewwz5cnw92tz0pc8qcuufvq7khhr8wpald05e92xw006sq94mg8v2ndf4sefvf9sygkshp5zfem29trqq2yxxz7", Network.Main);
            Assert.Equal("3925b6f67e2c340036ed12093dd44e0368df1b6ea26c53dbe4811f58fd5db8c1", p.DescriptionHash.ToString());

            p = BOLT11PaymentRequest.Parse("lntb20m1pvjluezhp58yjmdan79s6qqdhdzgynm4zwqd5d7xmw5fk98klysy043l2ahrqspp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqfpp3x9et2e20v6pu37c5d9vax37wxq72un98kmzzhznpurw9sgl2v0nklu2g4d0keph5t7tj9tcqd8rexnd07ux4uv2cjvcqwaxgj7v4uwn5wmypjd5n69z2xm3xgksg28nwht7f6zspwp3f9t", Network.TestNet);
            hash = new uint256(Encoders.Hex.DecodeData("00c17b39642becc064615ef196a6cc0cce262f1d8dde7b3c23694aeeda473abe"));
            Assert.True(key.PubKey.Verify(hash, p.ECDSASignature));
            Assert.True(p.VerifySignature());
            Assert.Equal("mk2QpYatsKicvFVuTAQLBryyccRXMUaGHP", p.FallbackAddresses[0].ToString());

            p = BOLT11PaymentRequest.Parse("lnbc20m1pvjluezhp58yjmdan79s6qqdhdzgynm4zwqd5d7xmw5fk98klysy043l2ahrqspp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqfppj3a24vwu6r8ejrss3axul8rxldph2q7z9kmrgvr7xlaqm47apw3d48zm203kzcq357a4ls9al2ea73r8jcceyjtya6fu5wzzpe50zrge6ulk4nvjcpxlekvmxl6qcs9j3tz0469gq5g658y", Network.Main);
            hash = new uint256(Encoders.Hex.DecodeData("64f1ff500bcc62a1b211cd6db84a1d93d1f77c6a132904465b6ff912420176be"));
            Assert.True(key.PubKey.Verify(hash, p.ECDSASignature));
            Assert.True(p.VerifySignature());
            Assert.Equal(key.PubKey, p.GetPayeePubKey());
            Assert.Equal("3EktnHQD7RiAE6uzMj2ZifT9YgRrkSgzQX", p.FallbackAddresses[0].ToString());

            p = BOLT11PaymentRequest.Parse("lnbc20m1pvjluezhp58yjmdan79s6qqdhdzgynm4zwqd5d7xmw5fk98klysy043l2ahrqspp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqfppqw508d6qejxtdg4y5r3zarvary0c5xw7kepvrhrm9s57hejg0p662ur5j5cr03890fa7k2pypgttmh4897d3raaq85a293e9jpuqwl0rnfuwzam7yr8e690nd2ypcq9hlkdwdvycqa0qza8", Network.Main);
            Assert.Equal("lnbc20m1pvjluezhp58yjmdan79s6qqdhdzgynm4zwqd5d7xmw5fk98klysy043l2ahrqspp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqfppqw508d6qejxtdg4y5r3zarvary0c5xw7kepvrhrm9s57hejg0p662ur5j5cr03890fa7k2pypgttmh4897d3raaq85a293e9jpuqwl0rnfuwzam7yr8e690nd2ypcq9hlkdwdvycqa0qza8", p.ToString());
            hash = new uint256(Encoders.Hex.DecodeData("b3df27aaa01d891cc9de272e7609557bdf4bd6fd836775e4470502f71307b627"));
            Assert.True(key.PubKey.Verify(hash, p.ECDSASignature));
            Assert.True(p.VerifySignature());
            Assert.Equal(key.PubKey, p.GetPayeePubKey());
            Assert.Equal("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4", p.FallbackAddresses[0].ToString());

            p = BOLT11PaymentRequest.Parse("lnbc20m1pvjluezhp58yjmdan79s6qqdhdzgynm4zwqd5d7xmw5fk98klysy043l2ahrqspp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqfp4qrp33g0q5c5txsp9arysrx4k6zdkfs4nce4xj0gdcccefvpysxf3q28j0v3rwgy9pvjnd48ee2pl8xrpxysd5g44td63g6xcjcu003j3qe8878hluqlvl3km8rm92f5stamd3jw763n3hck0ct7p8wwj463cql26ava", Network.Main);
            hash = new uint256(Encoders.Hex.DecodeData("399a8b167029fda8564fd2e99912236b0b8017e7d17e416ae17307812c92cf42"));
            Assert.True(key.PubKey.Verify(hash, p.ECDSASignature));
            Assert.True(p.VerifySignature());
            Assert.Equal(key.PubKey, p.GetPayeePubKey());
            Assert.Equal("bc1qrp33g0q5c5txsp9arysrx4k6zdkfs4nce4xj0gdcccefvpysxf3qccfmv3", p.FallbackAddresses[0].ToString());

            p = BOLT11PaymentRequest.Parse("lnbc20m1pvjluezpp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqhp58yjmdan79s6qqdhdzgynm4zwqd5d7xmw5fk98klysy043l2ahrqsfpp3qjmp7lwpagxun9pygexvgpjdc4jdj85fr9yq20q82gphp2nflc7jtzrcazrra7wwgzxqc8u7754cdlpfrmccae92qgzqvzq2ps8pqqqqqqpqqqqq9qqqvpeuqafqxu92d8lr6fvg0r5gv0heeeqgcrqlnm6jhphu9y00rrhy4grqszsvpcgpy9qqqqqqgqqqqq7qqzqj9n4evl6mr5aj9f58zp6fyjzup6ywn3x6sk8akg5v4tgn2q8g4fhx05wf6juaxu9760yp46454gpg5mtzgerlzezqcqvjnhjh8z3g2qqdhhwkj", Network.Main);
            hash = new uint256(Encoders.Hex.DecodeData("ff68246c5ad4b48c90cf8ff3b33b5cea61e62f08d0e67910ffdce1edecade71b"));
            Assert.True(p.VerifySignature());
            Assert.Equal(key.PubKey, p.GetPayeePubKey());
            Assert.Equal("1RustyRX2oai4EYYDpQGWvEL62BBGqN9T", p.FallbackAddresses[0].ToString());
            Assert.Single(p.Routes);
            Assert.Equal(2, p.Routes[0].Hops.Count);
            Assert.Equal("029e03a901b85534ff1e92c43c74431f7ce72046060fcf7a95c37e148f78c77255", p.Routes[0].Hops[0].PubKey.ToString());
            Assert.Equal("0102030405060708", p.Routes[0].Hops[0].ShortChannelId);
            Assert.Equal(LightMoney.FromUnit(1m, LightMoneyUnit.MilliSatoshi), p.Routes[0].Hops[0].FeeBase);
            Assert.Equal(20m / 1_000_000m, p.Routes[0].Hops[0].FeeProportional);
            Assert.Equal(3, p.Routes[0].Hops[0].CLTVExpiryDelay);
            Assert.Equal("039e03a901b85534ff1e92c43c74431f7ce72046060fcf7a95c37e148f78c77255", p.Routes[0].Hops[1].PubKey.ToString());
            Assert.Equal("030405060708090a", p.Routes[0].Hops[1].ShortChannelId);
            Assert.Equal(LightMoney.FromUnit(2m, LightMoneyUnit.MilliSatoshi), p.Routes[0].Hops[1].FeeBase);
            Assert.Equal(30m / 1_000_000m, p.Routes[0].Hops[1].FeeProportional);
            Assert.Equal(4, p.Routes[0].Hops[1].CLTVExpiryDelay);

            p = BOLT11PaymentRequest.Parse("lnbc9678785340p1pwmna7lpp5gc3xfm08u9qy06djf8dfflhugl6p7lgza6dsjxq454gxhj9t7a0sd8dgfkx7cmtwd68yetpd5s9xar0wfjn5gpc8qhrsdfq24f5ggrxdaezqsnvda3kkum5wfjkzmfqf3jkgem9wgsyuctwdus9xgrcyqcjcgpzgfskx6eqf9hzqnteypzxz7fzypfhg6trddjhygrcyqezcgpzfysywmm5ypxxjemgw3hxjmn8yptk7untd9hxwg3q2d6xjcmtv4ezq7pqxgsxzmnyyqcjqmt0wfjjq6t5v4khxxqyjw5qcqp2rzjq0gxwkzc8w6323m55m4jyxcjwmy7stt9hwkwe2qxmy8zpsgg7jcuwz87fcqqeuqqqyqqqqlgqqqqn3qq9qs4x9qlmd57lq7wwr23n3a6pkayy3jpfucyptlncs2maswe3dnnjy3ce2cgrvykmxlfpvn6ptqfqz4df5uaulvd4hjkckuqxrqqkz8jgphputwh", Network.Main);
            Assert.Equal("967878534", p.MinimumAmount.MilliSatoshi.ToString());
            Assert.Equal("0.00967878534", p.MinimumAmount.ToString());
        }

        [Fact]
        public void CanUseLightMoney()
        {
            var light = LightMoney.MilliSatoshis(1);
            Assert.Equal("0.00000000001", light.ToString());

            light = LightMoney.MilliSatoshis(200000);
            Assert.Equal(200m, light.ToDecimal(LightMoneyUnit.Satoshi));
            Assert.Equal(0.00000001m * 200m, light.ToDecimal(LightMoneyUnit.BTC));

            light = LightMoney.MilliSatoshis(200000) * 2;
            Assert.Equal(LightMoney.MilliSatoshis(400000), light);
            light = 2 * LightMoney.MilliSatoshis(200000);
            Assert.Equal(LightMoney.MilliSatoshis(400000), light);
            light = LightMoney.MilliSatoshis(200000) * 2L;
            Assert.Equal(LightMoney.MilliSatoshis(400000), light);
            light = 2L * LightMoney.MilliSatoshis(200000);
            Assert.Equal(LightMoney.MilliSatoshis(400000), light);

            var splitted = LightMoney.MilliSatoshis(12329183).Split(3).ToArray();
            Assert.Equal(LightMoney.MilliSatoshis(4109728), splitted[0]);
            Assert.Equal(LightMoney.MilliSatoshis(4109728), splitted[1]);
            Assert.Equal(LightMoney.MilliSatoshis(4109727), splitted[2]);
        }

        [Fact]
        public void CanParseLightningURL()
        {
            Assert.True(LightningConnectionString.TryParse("/test/a", true, out var conn));
            for (var i = 0; i < 2; i++)
            {
                if (i == 1)
                    Assert.True(LightningConnectionString.TryParse(conn.ToString(), false, out conn));
                Assert.Equal(i == 0, conn.IsLegacy);
                Assert.Equal("type=clightning;server=unix://test/a", conn.ToString());
                Assert.Equal("unix://test/a", conn.ToUri(true).AbsoluteUri);
                Assert.Equal("unix://test/a", conn.ToUri(false).AbsoluteUri);
                Assert.Equal(LightningConnectionType.CLightning, conn.ConnectionType);
            }

            Assert.True(LightningConnectionString.TryParse("unix://test/a", true, out conn));
            for (var i = 0; i < 2; i++)
            {
                if (i == 1)
                    Assert.True(LightningConnectionString.TryParse(conn.ToString(), false, out conn));
                Assert.Equal("type=clightning;server=unix://test/a", conn.ToString());
                Assert.Equal("unix://test/a", conn.ToUri(true).AbsoluteUri);
                Assert.Equal("unix://test/a", conn.ToUri(false).AbsoluteUri);
                Assert.Equal(LightningConnectionType.CLightning, conn.ConnectionType);
            }

            Assert.True(LightningConnectionString.TryParse("unix://test/a", true, out conn));
            for (var i = 0; i < 2; i++)
            {
                if (i == 1)
                    Assert.True(LightningConnectionString.TryParse(conn.ToString(), false, out conn));
                Assert.Equal("type=clightning;server=unix://test/a", conn.ToString());
                Assert.Equal("unix://test/a", conn.ToUri(true).AbsoluteUri);
                Assert.Equal("unix://test/a", conn.ToUri(false).AbsoluteUri);
                Assert.Equal(LightningConnectionType.CLightning, conn.ConnectionType);
            }

            Assert.True(LightningConnectionString.TryParse("tcp://test/a", true, out conn));
            for (var i = 0; i < 2; i++)
            {
                if (i == 1)
                    Assert.True(LightningConnectionString.TryParse(conn.ToString(), false, out conn));
                Assert.Equal("type=clightning;server=tcp://test/a", conn.ToString());
                Assert.Equal("tcp://test/a", conn.ToUri(true).AbsoluteUri);
                Assert.Equal("tcp://test/a", conn.ToUri(false).AbsoluteUri);
                Assert.Equal(LightningConnectionType.CLightning, conn.ConnectionType);
            }

            Assert.True(LightningConnectionString.TryParse("http://aaa:bbb@test/a", true, out conn));
            for (var i = 0; i < 2; i++)
            {
                if (i == 1)
                    Assert.True(LightningConnectionString.TryParse(conn.ToString(), false, out conn));
                Assert.Equal("type=charge;server=http://aaa:bbb@test/a;allowinsecure=true", conn.ToString());
                Assert.Equal("http://aaa:bbb@test/a", conn.ToUri(true).AbsoluteUri);
                Assert.Equal("http://test/a", conn.ToUri(false).AbsoluteUri);
                Assert.Equal(LightningConnectionType.Charge, conn.ConnectionType);
                Assert.Equal("aaa", conn.Username);
                Assert.Equal("bbb", conn.Password);
            }

            Assert.True(LightningConnectionString.TryParse("http://api-token:bbb@test/a", true, out conn));
            for (var i = 0; i < 2; i++)
            {
                if (i == 1)
                    Assert.True(LightningConnectionString.TryParse(conn.ToString(), false, out conn));
                Assert.Equal("type=charge;server=http://test/a;api-token=bbb;allowinsecure=true", conn.ToString());
            }

            Assert.False(LightningConnectionString.TryParse("lol://aaa:bbb@test/a", true, out conn));
            Assert.False(LightningConnectionString.TryParse("https://test/a", true, out conn));
            Assert.False(LightningConnectionString.TryParse("unix://dwewoi:dwdwqd@test/a", true, out conn));
            Assert.False(LightningConnectionString.TryParse("tcp://test/a", false, out conn));
            Assert.False(LightningConnectionString.TryParse("type=charge;server=http://aaa:bbb@test/a;unk=lol", false, out conn));
            Assert.False(LightningConnectionString.TryParse("type=charge;server=tcp://aaa:bbb@test/a", false, out conn));
            Assert.False(LightningConnectionString.TryParse("type=charge", false, out conn));
            Assert.False(LightningConnectionString.TryParse("type=clightning", false, out conn));
            Assert.True(LightningConnectionString.TryParse("type=clightning;server=tcp://aaa:bbb@test/a", false, out conn));
            Assert.True(LightningConnectionString.TryParse("type=clightning;server=/aaa:bbb@test/a", false, out conn));
            Assert.True(LightningConnectionString.TryParse("type=clightning;server=unix://aaa:bbb@test/a", false, out conn));
            Assert.False(LightningConnectionString.TryParse("type=clightning;server=wtf://aaa:bbb@test/a", false, out conn));

            var macaroon = "0201036c6e640247030a10b0dbbde28f009f83d330bde05075ca251201301a160a0761646472657373120472656164120577726974651a170a08696e766f6963657312047265616412057772697465000006200ae088692e67cf14e767c3d2a4a67ce489150bf810654ff980e1b7a7e263d5e8";
            var restrictedmacaroon = "0301036c6e640247030a10b0dbbde28f009f83d330bde05075ca251201301a160a0761646472657373120472656164120577726974651a170a08696e766f6963657312047265616412057772697465000006200ae088692e67cf14e767c3d2a4a67ce489150bf810654ff980e1b7a7e263d5e8";

            var certthumbprint = "c51bb1d402306d0da00e85581b32aa56166bcbab7eb888ff925d7167eb436d06";

            // We get this format from "openssl x509 -noout -fingerprint -sha256 -inform pem -in <certificate>"
            var certthumbprint2 = "C5:1B:B1:D4:02:30:6D:0D:A0:0E:85:58:1B:32:AA:56:16:6B:CB:AB:7E:B8:88:FF:92:5D:71:67:EB:43:6D:06";

            var lndUri = $"type=lnd-rest;server=https://lnd:lnd@127.0.0.1:53280/;macaroon={macaroon};certthumbprint={certthumbprint}";
            var lndUri2 = $"type=lnd-rest;server=https://lnd:lnd@127.0.0.1:53280/;macaroon={macaroon};certthumbprint={certthumbprint2}";
            var lndUri3 = $"type=lnd-rest;server=https://lnd:lnd@127.0.0.1:53280/;macaroon={macaroon};restrictedmacaroon={restrictedmacaroon}";
            var lndUri4 = $"type=lnd-rest;server=https://lnd:lnd@127.0.0.1:53280/;macaroon={macaroon};macaroondirectorypath=path";

            var certificateHash = new X509Certificate2(Encoders.Hex.DecodeData("2d2d2d2d2d424547494e2043455254494649434154452d2d2d2d2d0a4d494942396a4343415a7967417749424167495156397a62474252724e54716b4e4b55676d72524d377a414b42676771686b6a4f50515144416a41784d5238770a485159445651514b45785a73626d5167595856306232646c626d56795958526c5a43426a5a584a304d51347744415944565151444577564754304e56557a41650a467730784f4441304d6a55794d7a517a4d6a4261467730784f5441324d6a41794d7a517a4d6a42614d444578487a416442674e5642416f54466d78755a4342680a645852765a3256755a584a686447566b49474e6c636e5178446a414d42674e5642414d5442555a50513156544d466b77457759484b6f5a497a6a3043415159490a4b6f5a497a6a304441516344516741454b7557424568564f75707965434157476130766e713262712f59396b41755a78616865646d454553482b753936436d450a397577486b4b2b4a7667547a66385141783550513741357254637155374b57595170303175364f426c5443426b6a414f42674e56485138424166384542414d430a4171517744775944565230544151482f42415577417745422f7a427642674e56485245456144426d6767564754304e565534494a6247396a5957786f62334e300a6877522f4141414268784141414141414141414141414141414141414141414268775373474f69786877514b41457342687753702f717473687754417141724c0a687753702f6d4a72687753702f754f77687753702f714e59687753702f6874436877514b70514157687753702f6c42514d416f4743437147534d343942414d430a413067414d45554349464866716d595a5043647a4a5178386b47586859473834394c31766541364c784d6f7a4f5774356d726835416945413662756e51556c710a6558553070474168776c3041654d726a4d4974394c7652736179756162565a593278343d0a2d2d2d2d2d454e442043455254494649434154452d2d2d2d2d0a"))
                            .GetCertHash(HashAlgorithmName.SHA256);

            Assert.True(LightningConnectionString.TryParse(lndUri, false, out conn));
            Assert.True(LightningConnectionString.TryParse(lndUri2, false, out var conn2));
            Assert.True(LightningConnectionString.TryParse(lndUri3, false, out var conn3));
            Assert.True(LightningConnectionString.TryParse(lndUri4, false, out var conn4));
            Assert.Equal(lndUri4, conn4.ToString());
            Assert.Equal(conn2.ToString(), conn.ToString());
            Assert.Equal(lndUri, conn.ToString());
            Assert.Equal(LightningConnectionType.LndREST, conn.ConnectionType);
            Assert.Equal(macaroon, Encoders.Hex.EncodeData(conn.Macaroon));
            Assert.Equal(certthumbprint.Replace(":", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant(), Encoders.Hex.EncodeData(conn.CertificateThumbprint));
            Assert.True(certificateHash.SequenceEqual(conn.CertificateThumbprint));

            // AllowInsecure can be set to allow http
            Assert.False(LightningConnectionString.TryParse($"type=lnd-rest;server=http://127.0.0.1:53280/;macaroon={macaroon};allowinsecure=false", false, out conn2));
            Assert.True(LightningConnectionString.TryParse($"type=lnd-rest;server=http://127.0.0.1:53280/;macaroon={macaroon};allowinsecure=true", false, out conn2));
            Assert.True(LightningConnectionString.TryParse($"type=lnd-rest;server=http://127.0.0.1:53280/;macaroon={macaroon};allowinsecure=true", false, out conn2));
            Assert.True(LightningConnectionString.TryParse("type=charge;server=http://test/a;cookiefilepath=path;allowinsecure=true", false, out conn));
            Assert.Equal("path", conn.CookieFilePath);
            Assert.Equal("type=charge;server=http://test/a;cookiefilepath=path;allowinsecure=true", conn.ToString());

            // Should not have cookiefilepath and api-token at once
            Assert.False(LightningConnectionString.TryParse("type=charge;server=http://test/a;cookiefilepath=path;api-token=abc", false, out conn));

            // Should not have cookiefilepath and api-token at once
            Assert.False(LightningConnectionString.TryParse("type=charge;server=http://api-token:blah@test/a;cookiefilepath=path", false, out conn));
            Assert.True(LightningConnectionString.TryParse("type=charge;server=http://api-token:foiewnccewuify@127.0.0.1:54938/;allowinsecure=true", out conn));
            Assert.Equal("type=charge;server=http://127.0.0.1:54938/;api-token=foiewnccewuify;allowinsecure=true", conn.ToString());
            Assert.True(LightningConnectionString.TryParse("type=lnbank;server=https://mybtcpay.com/;api-token=myapitoken", false, out conn));
            
            Assert.True(LightningConnectionString.TryParse("type=lndhub;server=https://mylndhub:password@lndhub.io/", false, out conn));
            Assert.Equal("mylndhub", conn.Username);
            Assert.Equal("password", conn.Password);
        }

        private static async Task<RPCClient> GetRPCClient()
        {
            var client = Tester.CreateRPC();
            await client.ScanRPCCapabilitiesAsync();
            return client;
        }

        private async Task<LightningInvoice> GetPaidInvoice(ILightningInvoiceListener listener, Task<LightningInvoice> waiting, string invoiceId)
        {
            LightningInvoice invoice;
            while (true)
            {
                invoice = await waiting;
                if (invoice.Id == invoiceId)
                {
                    break;
                }
                waiting = listener.WaitInvoice(default);
            }

            return invoice;
        }

        private static void AssertUnpaid(LightningInvoice invoice, LightMoney expectedAmount = null)
        {
            expectedAmount ??= LightMoney.MilliSatoshis(10000);
            Assert.NotNull(invoice.BOLT11);
            Assert.Equal(expectedAmount, invoice.Amount);
            Assert.Null(invoice.PaidAt);
            Assert.Equal(LightningInvoiceStatus.Unpaid, invoice.Status);
        }

        private async Task EnsureConnectedToDestinations((string Name, ILightningClient Customer, ILightningClient Merchant) test)
        {
            await Task.WhenAll(
                WaitServersAreUp($"{test.Name} Customer ({(await test.Customer.GetInfo()).NodeInfoList.Select(e => e.NodeId).First()}):", test.Customer),
                WaitServersAreUp($"{test.Name} Merchant ({(await test.Merchant.GetInfo()).NodeInfoList.Select(e => e.NodeId).First()}):", test.Merchant));
            Logs.Tester.LogInformation($"{test.Name}: Connecting channels...");
            
            var cashcow = await GetRPCClient();
            var customerNodeClient = test.Customer is LndHubLightningClient ? Tester.CreateLndClient() : test.Customer;
            var merchantNodeClient = test.Merchant is LndHubLightningClient ? Tester.CreateLndClientDest() : test.Merchant;
            await ConnectChannels.ConnectAll(cashcow, new[] { customerNodeClient }, new[] { merchantNodeClient });
            Logs.Tester.LogInformation($"{test.Name}: Channels connected");
            
            var channelsCustomer = await customerNodeClient.ListChannels();
            var channelsMerchant = await merchantNodeClient.ListChannels();
            foreach (var channel in channelsCustomer)
            {
                Logs.Tester.LogInformation($"Customer to {channel.RemoteNode}: Capacity = {channel.Capacity} BTC, Local Balance = {channel.LocalBalance} BTC");
            }
            foreach (var channel in channelsMerchant)
            {
                Logs.Tester.LogInformation($"Merchant to {channel.RemoteNode}: Capacity = {channel.Capacity} BTC, Local Balance = {channel.LocalBalance} BTC");
            }
            Logs.Tester.LogInformation("-----------------");
        }

        private async Task FundLndHubWallet(ILightningClient receiver, LightMoney amount)
        {
            // The customers LNDhub wallet needs some initial funds.
            // Connect to destination first - implicitly through the connected LND instance
            ILightningClient dest = Tester.CreateLndClientDest();
            await EnsureConnectedToDestinations(("LNDhub", receiver, dest));
            // Fund the LNDhub account with some sats
            var fundingInvoice = await receiver.CreateInvoice(amount, "FundLndHubWallet", TimeSpan.FromMinutes(1));
            var resp = await dest.Pay(fundingInvoice.BOLT11);
            Assert.Equal(PayResult.Ok, resp.Result);
        }
    }
}
