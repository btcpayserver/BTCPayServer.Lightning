using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Charge;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Lightning.Eclair;
using BTCPayServer.Lightning.LND;
using BTCPayServer.Lightning.LndHub;
using NBitcoin;
using NBitcoin.RPC;

namespace BTCPayServer.Lightning.Tests
{
    public static class Tester
    {
        public static Network Network => Network.RegTest;

        public static RPCClient CreateRPC()
        {
            var host = CommonTests.Docker ? "bitcoind:43782" : "127.0.0.1:37393";
            return new RPCClient("ceiwHEbqWI83:DwubwWsoo3", host, Network);
        }

        private static ChargeClient CreateChargeClient()
        {
            var host = CommonTests.Docker ? "charge:9112" : "127.0.0.1:37462";
            var uri = new Uri($"http://api-token:foiewnccewuify@{host}");
            return new ChargeClient(uri, Network, allowInsecure: true);
        }

        internal static LndClient CreateLndClient()
        {
            var host = CommonTests.Docker ? "http://lnd:8080" : "http://127.0.0.1:32736";
            return new LndClient(new LndRestSettings { AllowInsecure = true, Uri = new Uri(host) }, Network.RegTest);
        }

        internal static LndClient CreateLndClientDest()
        {
            var host = CommonTests.Docker ? "http://lnd_dest:8080" : "http://127.0.0.1:42802";
            return new LndClient(new LndRestSettings { AllowInsecure = true, Uri = new Uri(host) }, Network.RegTest);
        }

        private static CLightningClient CreateCLightningClient()
        {
            var host = CommonTests.Docker ? "tcp://lightningd:9835" : "tcp://127.0.0.1:48532";
            return new CLightningClient(new Uri(host), Network);
        }

        private static CLightningClient CreateCLightningClientDest()
        {
            var host = CommonTests.Docker ? "tcp://lightningd_dest:9835" : "tcp://127.0.0.1:42549";
            return new CLightningClient(new Uri(host), Network);
        }

        private static EclairLightningClient CreateEclairClient()
        {
            var host = CommonTests.Docker ? "http://eclair:8080" : "http://127.0.0.1:4570";
            return new EclairLightningClient(new Uri(host), "bukkake", Network);
        }

        private static EclairLightningClient CreateEclairClientDest()
        {
            var host = CommonTests.Docker ? "http://eclair_dest:8080" : "http://127.0.0.1:4571";
            return new EclairLightningClient(new Uri(host), "bukkake", Network);
        }

        private static async Task<LndHubLightningClient> CreateLndHubClient()
        {
            var uri = new Uri(CommonTests.Docker ? "http://lndhub:3000" : "http://127.0.0.1:42923");
            var client = new LndHubLightningClient(uri, "login", "password", Network.RegTest);
            var data = await client.CreateAccount();
            return new LndHubLightningClient(uri, data.Login, data.Password, Network.RegTest);
        }

        private static async Task<LndHubLightningClient> CreateLndHubClientDest()
        {
            var uri = new Uri(CommonTests.Docker ? "http://lndhub_dest:3000" : "http://127.0.0.1:42924");
            var client = new LndHubLightningClient(uri, "login", "password", Network.RegTest);
            var data = await client.CreateAccount();
            return new LndHubLightningClient(uri, data.Login, data.Password, Network.RegTest);
        }

        public static IEnumerable<(string Name, ILightningClient Client)> GetLightningClients()
        {
            // Lightning charge isn't compatible with some breaking changes of Core lightning v23.5
            // yield return ("Charge (Client)", CreateChargeClient());
            yield return ("C-Lightning (Client)", CreateCLightningClient());
            yield return ("LND (Client)", CreateLndClient());
            yield return ("Eclair (Client)", CreateEclairClient());
            yield return ("LNDhub (Client)", CreateLndHubClient().Result);
        }

        public static IEnumerable<(string Name, ILightningClient Customer, ILightningClient Merchant)> GetTestedPairs()
        {
            yield return ("C-Lightning", CreateCLightningClient(), CreateCLightningClientDest());
            yield return ("LND", CreateLndClient(), CreateLndClientDest());
            yield return ("Eclair", CreateEclairClient(), CreateEclairClientDest());
            yield return ("LNDhub", CreateLndHubClient().Result, CreateLndHubClientDest().Result);
        }
    }
}
