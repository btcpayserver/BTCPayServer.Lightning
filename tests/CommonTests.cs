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

namespace BTCPayServer.Lightning.Tests
{
    public class CommonTests
    {
        [Fact]
        public async Task CanCreateInvoice()
        {
            foreach(var client in Tester.GetLightningClients())
            {
                var createdInvoice = await client.CreateInvoice(10000, "CanCreateInvoice", TimeSpan.FromMinutes(5));
                var retrievedInvoice = await client.GetInvoice(createdInvoice.Id);
                AssertUnpaid(createdInvoice);
                AssertUnpaid(retrievedInvoice);
            }
        }

        [Fact]
        public async Task CanCreateInvoiceUsingConnectionString()
        {
            ILightningClientFactory factory = new LightningClientFactory(Tester.Network);
            foreach(var connectionString in new[]
            {
                "type=charge;server=http://api-token:foiewnccewuify@127.0.0.1:37462",
                "type=lnd-rest;server=https://127.0.0.1:42802;allowinsecure=true",
                "type=clightning;server=tcp://127.0.0.1:48532"
            })
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
            await EnsureConnectedToDestinations();
            var blockHeight = Tester.CreateRPC().GetBlockCount();
            foreach(var client in Tester.GetLightningClients())
            {
                var info = await client.GetInfo();
                Assert.NotNull(info);
                Assert.Equal(blockHeight, info.BlockHeight);
                Assert.NotNull(info.NodeInfo);
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
                    var invoice = await dest.CreateInvoice(10000, "CanPayInvoiceAndReceive", TimeSpan.FromSeconds(5000));
                    using(var listener = await dest.Listen())
                    {
                        var waiting = listener.WaitInvoice(default);
                        var paidReply = await client.Pay(invoice.BOLT11);
                        Assert.Equal(PayResult.Ok, paidReply.Result);
                        var paidInvoice = await waiting;
                        Assert.Equal(LightningInvoiceStatus.Paid, paidInvoice.Status);
                        var retrievedInvoice = await dest.GetInvoice(invoice.Id);
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
                    var merchantInvoice = await dest.CreateInvoice(10000, "Hello world", TimeSpan.FromSeconds(3600));
                    var merchantInvoice2 = await dest.CreateInvoice(10000, "Hello world", TimeSpan.FromSeconds(3600));

                    var waitToken = default(CancellationToken);
                    var listener = await dest.Listen(waitToken);
                    var waitTask = listener.WaitInvoice(waitToken);

                    var payResponse = await src.Pay(merchantInvoice.BOLT11);

                    var invoice = await waitTask;
                    Assert.True(invoice.PaidAt.HasValue);

                    var waitTask2 = listener.WaitInvoice(waitToken);

                    payResponse = await src.Pay(merchantInvoice2.BOLT11);

                    invoice = await waitTask2;
                    Assert.True(invoice.PaidAt.HasValue);

                    var waitTask3 = listener.WaitInvoice(waitToken);
                    await Task.Delay(100);
                    listener.Dispose();
                    Assert.Throws<OperationCanceledException>(() => waitTask3.GetAwaiter().GetResult());
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
            return ConnectChannels.ConnectAll(Tester.CreateRPC(), Tester.GetLightningSenderClients(), Tester.GetLightningDestClients());
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

            var certthumbprint = "c51bb1d402306d0da00e85581b32aa56166bcbab7eb888ff925d7167eb436d06";

            // We get this format from "openssl x509 -noout -fingerprint -sha256 -inform pem -in <certificate>"
            var certthumbprint2 = "C5:1B:B1:D4:02:30:6D:0D:A0:0E:85:58:1B:32:AA:56:16:6B:CB:AB:7E:B8:88:FF:92:5D:71:67:EB:43:6D:06";

            var lndUri = $"type=lnd-rest;server=https://lnd:lnd@127.0.0.1:53280/;macaroon={macaroon};certthumbprint={certthumbprint}";
            var lndUri2 = $"type=lnd-rest;server=https://lnd:lnd@127.0.0.1:53280/;macaroon={macaroon};certthumbprint={certthumbprint2}";

            var certificateHash = new X509Certificate2(Encoders.Hex.DecodeData("2d2d2d2d2d424547494e2043455254494649434154452d2d2d2d2d0a4d494942396a4343415a7967417749424167495156397a62474252724e54716b4e4b55676d72524d377a414b42676771686b6a4f50515144416a41784d5238770a485159445651514b45785a73626d5167595856306232646c626d56795958526c5a43426a5a584a304d51347744415944565151444577564754304e56557a41650a467730784f4441304d6a55794d7a517a4d6a4261467730784f5441324d6a41794d7a517a4d6a42614d444578487a416442674e5642416f54466d78755a4342680a645852765a3256755a584a686447566b49474e6c636e5178446a414d42674e5642414d5442555a50513156544d466b77457759484b6f5a497a6a3043415159490a4b6f5a497a6a304441516344516741454b7557424568564f75707965434157476130766e713262712f59396b41755a78616865646d454553482b753936436d450a397577486b4b2b4a7667547a66385141783550513741357254637155374b57595170303175364f426c5443426b6a414f42674e56485138424166384542414d430a4171517744775944565230544151482f42415577417745422f7a427642674e56485245456144426d6767564754304e565534494a6247396a5957786f62334e300a6877522f4141414268784141414141414141414141414141414141414141414268775373474f69786877514b41457342687753702f717473687754417141724c0a687753702f6d4a72687753702f754f77687753702f714e59687753702f6874436877514b70514157687753702f6c42514d416f4743437147534d343942414d430a413067414d45554349464866716d595a5043647a4a5178386b47586859473834394c31766541364c784d6f7a4f5774356d726835416945413662756e51556c710a6558553070474168776c3041654d726a4d4974394c7652736179756162565a593278343d0a2d2d2d2d2d454e442043455254494649434154452d2d2d2d2d0a"))
                            .GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256);

            Assert.True(LightningConnectionString.TryParse(lndUri, false, out conn));
            Assert.True(LightningConnectionString.TryParse(lndUri2, false, out var conn2));
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
        }
    }
}
