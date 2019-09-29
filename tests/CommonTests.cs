using System;
using System.Threading.Tasks;
using Xunit;
using System.Linq;
using NBitcoin;
using NBitcoin.RPC;
using NBitcoin.DataEncoders;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Threading;
using NBitcoin.Crypto;
using Xunit.Abstractions;

namespace BTCPayServer.Lightning.Tests
{
    public class CommonTests
    {

        public CommonTests(ITestOutputHelper helper)
        {
            Docker = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IN_DOCKER_CONTAINER"));
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        public static bool Docker { get; set; }

        [Fact]
        public async Task CanCreateInvoice()
        {
            foreach(var client in Tester.GetLightningClients())
            {
                Logs.Tester.LogInformation($"{client.Name}: {nameof(CanCreateInvoice)}");
                var createdInvoice = await client.Client.CreateInvoice(10000, "CanCreateInvoice", TimeSpan.FromMinutes(5));
                var retrievedInvoice = await client.Client.GetInvoice(createdInvoice.Id);
                AssertUnpaid(createdInvoice);
                Assert.True(createdInvoice.ExpiresAt > DateTimeOffset.UtcNow);
                AssertUnpaid(retrievedInvoice);
                Assert.True(retrievedInvoice.ExpiresAt > DateTimeOffset.UtcNow);
            }
        }

        [Fact]
        public async Task CanCreateInvoiceUsingConnectionString()
        {
            ILightningClientFactory factory = new LightningClientFactory(Tester.Network);

            var connectionStrings = Docker
                ? new[]
                {
                    "type=charge;server=http://api-token:foiewnccewuify@charge:9112",
                    "type=lnd-rest;server=https://lnd_dest:8080;allowinsecure=true",
                    "type=clightning;server=tcp://lightningd:9835",
                    "type=eclair;server=http://eclair:8080;password=bukkake;bitcoin-host=bitcoind:43782;bitcoin-auth=ceiwHEbqWI83:DwubwWsoo3"

                }
                : new[]
                {
                    "type=charge;server=http://api-token:foiewnccewuify@127.0.0.1:37462",
                    "type=lnd-rest;server=https://127.0.0.1:42802;allowinsecure=true",
                    "type=clightning;server=tcp://127.0.0.1:48532",
                    "type=eclair;server=http://127.0.0.1:4570;password=bukkake;bitcoin-host=127.0.0.1:37393;bitcoin-auth=ceiwHEbqWI83:DwubwWsoo3"

                };
            foreach(var connectionString in connectionStrings)
            {
                ILightningClient client = factory.Create(connectionString);
                var createdInvoice = await client.CreateInvoice(10000, "CanCreateInvoice", TimeSpan.FromMinutes(5));
                var retrievedInvoice = await client.GetInvoice(createdInvoice.Id);
                AssertUnpaid(createdInvoice);
                AssertUnpaid(retrievedInvoice);
            }
        }

        [Fact]
        public async Task CanGetInfo()
        {
            var blockHeight = Tester.CreateRPC().GetBlockCount();
            foreach(var client in Tester.GetLightningClients())
            {
                Logs.Tester.LogInformation($"{client.Name}: {nameof(CanGetInfo)}");
                var info = await client.Client.GetInfo();
                Assert.NotNull(info);
                Assert.Equal(blockHeight, info.BlockHeight);
                Assert.NotEmpty(info.NodeInfoList);
            }
        }

        [Fact]
        public async Task CanPayInvoiceAndReceive()
        {
            await EnsureConnectedToDestinations();

            foreach(var client in Tester.GetLightningSenderClients())
            {
                foreach(var dest in Tester.GetLightningDestClients())
                {
                    Logs.Tester.LogInformation($"{client.Name} => {dest.Name}: {nameof(CanPayInvoiceAndReceive)}");
                    var invoice = await dest.Client.CreateInvoice(10000, "CanPayInvoiceAndReceive", TimeSpan.FromSeconds(5000));
                    using(var listener = await dest.Client.Listen())
                    {
                        var waiting = listener.WaitInvoice(default);
                        var paidReply = await client.Client.Pay(invoice.BOLT11);
                        Assert.Equal(PayResult.Ok, paidReply.Result);
                        var paidInvoice = await waiting;
                        Assert.Equal(LightningInvoiceStatus.Paid, paidInvoice.Status);
                        var retrievedInvoice = await dest.Client.GetInvoice(invoice.Id);
                        Assert.Equal(LightningInvoiceStatus.Paid, retrievedInvoice.Status);
                    }
                }
            }
        }

        [Fact]
        public async Task CanWaitListenInvoice()
        {
            await EnsureConnectedToDestinations();

            foreach(var src in Tester.GetLightningSenderClients())
            {
                foreach(var dest in Tester.GetLightningDestClients())
                {
                    Logs.Tester.LogInformation($"{src.Name} => {dest.Name}: {nameof(CanWaitListenInvoice)}");
                    var merchantInvoice = await dest.Client.CreateInvoice(10000, "Hello world", TimeSpan.FromSeconds(3600));
                    Logs.Tester.LogInformation($"{src.Name} => {dest.Name}: Created invoice {merchantInvoice.Id}");
                    var merchantInvoice2 = await dest.Client.CreateInvoice(10000, "Hello world", TimeSpan.FromSeconds(3600));
                    Logs.Tester.LogInformation($"{src.Name} => {dest.Name}: Created invoice {merchantInvoice2.Id}");

                    var waitToken = default(CancellationToken);
                    var listener = await dest.Client.Listen(waitToken);
                    var waitTask = listener.WaitInvoice(waitToken);

                    var payResponse = await src.Client.Pay(merchantInvoice.BOLT11);
                    Logs.Tester.LogInformation($"{src.Name} => {dest.Name}: Paid invoice {merchantInvoice.Id}");

                    var invoice = await waitTask;
                    Logs.Tester.LogInformation($"{src.Name} => {dest.Name}: Notification received for {invoice.Id}");
                    Assert.Equal(invoice.Id, merchantInvoice.Id);
                    Assert.True(invoice.PaidAt.HasValue);

                    var waitTask2 = listener.WaitInvoice(waitToken);

                    payResponse = await src.Client.Pay(merchantInvoice2.BOLT11);
                    Logs.Tester.LogInformation($"{src.Name} => {dest.Name}: Paid invoice {merchantInvoice2.Id}");

                    invoice = await waitTask2;
                    Logs.Tester.LogInformation($"{src.Name} => {dest.Name}: Notification received for {invoice.Id}");

                    Assert.True(invoice.PaidAt.HasValue);

                    Assert.Equal(invoice.Amount, invoice.AmountReceived);
                    Assert.Equal(invoice.Id, merchantInvoice2.Id);
                    Assert.Equal(new LightMoney(10000, LightMoneyUnit.MilliSatoshi), invoice.Amount);
                    var waitTask3 = listener.WaitInvoice(waitToken);
                    await Task.Delay(100);
                    listener.Dispose();
                    Logs.Tester.LogInformation($"{src.Name} => {dest.Name}: Listener disposed, should throw exception");
                    Assert.Throws<OperationCanceledException>(() => waitTask3.GetAwaiter().GetResult());
                }
            }
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

        [Fact]
        public async Task CanListChannels()
        {
            await EnsureConnectedToDestinations();
            int channelCount = Tester.GetLightningSenderClients().Count();

            foreach (var sender in Tester.GetLightningSenderClients())
            {
                Logs.Tester.LogInformation($"{sender.Name}: {nameof(CanListChannels)}");
                var senderChannels = await sender.Client.ListChannels();
                var senderInfo = await sender.Client.GetInfo();
                Assert.NotEmpty(senderChannels);
                Assert.Equal(channelCount, senderChannels.Where(s => s.IsActive).GroupBy(s => s.RemoteNode).Count());

                foreach (var dest in Tester.GetLightningDestClients())
                {
                    var destChannels = await dest.Client.ListChannels();
                    var destInfo = await dest.Client.GetInfo();
                    Assert.NotEmpty(destChannels);
                    Assert.Equal(channelCount, destChannels.GroupBy(s => s.RemoteNode).Count());
                    foreach (var c in senderChannels)
                    {
                        Assert.NotNull(c.RemoteNode);
                        Assert.True(c.IsPublic);
                        Assert.True(c.IsActive);
                        Assert.NotNull(c.Capacity);
                        Assert.NotNull(c.LocalBalance);
                        Assert.NotNull(c.ChannelPoint);
                    }

                    Assert.Contains(senderChannels, c => c.RemoteNode.Equals(destInfo.NodeInfo.NodeId));
                    Assert.Contains(destChannels, c => c.RemoteNode.Equals(senderInfo.NodeInfo.NodeId));
                }
            }
        }


        private static void AssertUnpaid(LightningInvoice invoice)
        {
            Assert.NotNull(invoice.BOLT11);
            Assert.Equal(LightMoney.MilliSatoshis(10000), invoice.Amount);
            Assert.Null(invoice.PaidAt);
            Assert.Equal(LightningInvoiceStatus.Unpaid, invoice.Status);
        }

        private Task EnsureConnectedToDestinations()
        {
            return ConnectChannels.ConnectAll(Tester.CreateRPC(), Tester.GetLightningSenderClients().Select(c => c.Client).ToArray(), Tester.GetLightningDestClients().Select(c => c.Client).ToArray());
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

            var preimage = Encoders.Hex.DecodeData("6c6e62630b25fe64410d00004080c1014181c20240004080c1014181c20240004080c1014181c202404081a1fa83632b0b9b29031b7b739b4b232b91039bab83837b93a34b733903a3434b990383937b532b1ba0");
            var hash = new uint256(Encoders.Hex.DecodeData("c3d4e83f646fa79a393d75277b1d858db1d1f7ab7137dcb7835db2ecd518e1c9"));
            Assert.Equal(hash, new uint256(Hashes.SHA256(preimage)));
            Assert.Equal(hash, p.Hash);
            Assert.True(key.PubKey.Verify(hash, p.ECDSASignature));
            Assert.True(p.GetPayeePubKey().Verify(hash, p.ECDSASignature));
            Assert.True(p.VerifySignature());
            Assert.Equal(key.PubKey, p.GetPayeePubKey());

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
        }

        [Fact]
        public void CanUseLightMoney()
        {
            var light = LightMoney.MilliSatoshis(1);
            Assert.Equal("0.00000000001", light.ToString());

            light = LightMoney.MilliSatoshis(200000);
            Assert.Equal(200m, light.ToDecimal(LightMoneyUnit.Satoshi));
            Assert.Equal(0.00000001m * 200m, light.ToDecimal(LightMoneyUnit.BTC));
        }


        [Fact]
        public void CanParseLightningURL()
        {
            LightningConnectionString conn = null;
            Assert.True(LightningConnectionString.TryParse("/test/a", true, out conn));
            for(int i = 0; i < 2; i++)
            {
                if(i == 1)
                    Assert.True(LightningConnectionString.TryParse(conn.ToString(), false, out conn));
                Assert.Equal(i == 0, conn.IsLegacy);
                Assert.Equal("type=clightning;server=unix://test/a", conn.ToString());
                Assert.Equal("unix://test/a", conn.ToUri(true).AbsoluteUri);
                Assert.Equal("unix://test/a", conn.ToUri(false).AbsoluteUri);
                Assert.Equal(LightningConnectionType.CLightning, conn.ConnectionType);
            }

            Assert.True(LightningConnectionString.TryParse("unix://test/a", true, out conn));
            for(int i = 0; i < 2; i++)
            {
                if(i == 1)
                    Assert.True(LightningConnectionString.TryParse(conn.ToString(), false, out conn));
                Assert.Equal("type=clightning;server=unix://test/a", conn.ToString());
                Assert.Equal("unix://test/a", conn.ToUri(true).AbsoluteUri);
                Assert.Equal("unix://test/a", conn.ToUri(false).AbsoluteUri);
                Assert.Equal(LightningConnectionType.CLightning, conn.ConnectionType);
            }

            Assert.True(LightningConnectionString.TryParse("unix://test/a", true, out conn));
            for(int i = 0; i < 2; i++)
            {
                if(i == 1)
                    Assert.True(LightningConnectionString.TryParse(conn.ToString(), false, out conn));
                Assert.Equal("type=clightning;server=unix://test/a", conn.ToString());
                Assert.Equal("unix://test/a", conn.ToUri(true).AbsoluteUri);
                Assert.Equal("unix://test/a", conn.ToUri(false).AbsoluteUri);
                Assert.Equal(LightningConnectionType.CLightning, conn.ConnectionType);
            }

            Assert.True(LightningConnectionString.TryParse("tcp://test/a", true, out conn));
            for(int i = 0; i < 2; i++)
            {
                if(i == 1)
                    Assert.True(LightningConnectionString.TryParse(conn.ToString(), false, out conn));
                Assert.Equal("type=clightning;server=tcp://test/a", conn.ToString());
                Assert.Equal("tcp://test/a", conn.ToUri(true).AbsoluteUri);
                Assert.Equal("tcp://test/a", conn.ToUri(false).AbsoluteUri);
                Assert.Equal(LightningConnectionType.CLightning, conn.ConnectionType);
            }

            Assert.True(LightningConnectionString.TryParse("http://aaa:bbb@test/a", true, out conn));
            for(int i = 0; i < 2; i++)
            {
                if(i == 1)
                    Assert.True(LightningConnectionString.TryParse(conn.ToString(), false, out conn));
                Assert.Equal("type=charge;server=http://aaa:bbb@test/a", conn.ToString());
                Assert.Equal("http://aaa:bbb@test/a", conn.ToUri(true).AbsoluteUri);
                Assert.Equal("http://test/a", conn.ToUri(false).AbsoluteUri);
                Assert.Equal(LightningConnectionType.Charge, conn.ConnectionType);
                Assert.Equal("aaa", conn.Username);
                Assert.Equal("bbb", conn.Password);
            }

            Assert.True(LightningConnectionString.TryParse("http://api-token:bbb@test/a", true, out conn));
            for(int i = 0; i < 2; i++)
            {
                if(i == 1)
                    Assert.True(LightningConnectionString.TryParse(conn.ToString(), false, out conn));
                Assert.Equal("type=charge;server=http://test/a;api-token=bbb", conn.ToString());
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
                            .GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256);

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


            Assert.True(LightningConnectionString.TryParse("type=charge;server=http://test/a;cookiefilepath=path", false, out conn));
            Assert.Equal("path", conn.CookieFilePath);
            Assert.Equal("type=charge;server=http://test/a;cookiefilepath=path", conn.ToString());
            // Should not have cookiefilepath and api-token at once
            Assert.False(LightningConnectionString.TryParse("type=charge;server=http://test/a;cookiefilepath=path;api-token=abc", false, out conn));
            // Should not have cookiefilepath and api-token at once
            Assert.False(LightningConnectionString.TryParse("type=charge;server=http://api-token:blah@test/a;cookiefilepath=path", false, out conn));
        }
    }
}
