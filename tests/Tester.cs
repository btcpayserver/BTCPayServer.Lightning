using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Lightning.Eclair;
using BTCPayServer.Lightning.LND;
using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.Tests
{
    public static class Tester
    {
        public static Network Network => Network.RegTest;

        public static RPCClient CreateRPC()
        {
            var host = BaseTests.Docker ? "bitcoind:43782" : "127.0.0.1:37393";
            return new RPCClient("ceiwHEbqWI83:DwubwWsoo3", host, Network);
        }

        internal static LndClient CreateLndClient(HttpClient httpClient = null)
        {
            var host = BaseTests.Docker ? "http://lnd:8080" : "http://127.0.0.1:32736";
            var settings = new LndRestSettings { Uri = new Uri(host) };
            return new LndClient(new LndSwaggerClient(settings, httpClient), Network.RegTest);
        }

        internal static LndClient CreateLndClientDest()
        {
            var host = BaseTests.Docker ? "http://lnd_dest:8080" : "http://127.0.0.1:42802";
            return new LndClient(new LndRestSettings { Uri = new Uri(host) }, Network.RegTest);
        }

        private static CLightningClient CreateCLightningClient()
        {
            var host = BaseTests.Docker ? "tcp://lightningd:9835" : "tcp://127.0.0.1:48532";
            return new CLightningClient(new Uri(host), Network);
        }

        private static CLightningClient CreateCLightningClientDest()
        {
            var host = BaseTests.Docker ? "tcp://lightningd_dest:9835" : "tcp://127.0.0.1:42549";
            return new CLightningClient(new Uri(host), Network);
        }

        private static string ClnRestHost => BaseTests.Docker ? "http://lightningd:3010/" : "http://127.0.0.1:48533/";

        private static CLightningClient CreateCLightningRestClient()
        {
            return CreateCLightningRestClient(CreateCLightningClient(), ClnRestHost);
        }

        internal static async Task<CLightningClient> CreateRestrictedCLightningRestClient(JArray restrictions)
        {
            return new CLightningClient(await CreateClnRestSettings(CreateCLightningClient(), new Uri(ClnRestHost), restrictions), Network);
        }

        private static CLightningClient CreateCLightningRestClientDest()
        {
            var host = BaseTests.Docker ? "http://lightningd_dest:3010/" : "http://127.0.0.1:42550/";
            return CreateCLightningRestClient(CreateCLightningClientDest(), host);
        }

        static readonly ConcurrentDictionary<string, Lazy<Task<CLightningRestSettings>>> ClnRestSettings = new();

        private static CLightningClient CreateCLightningRestClient(CLightningClient rpc, string server)
        {
            // The setup runs once per server on the thread pool. Blocking on it from a test thread
            // can't deadlock, even when xunit runs everything on a single thread.
            var setup = ClnRestSettings.GetOrAdd(server, _ => new Lazy<Task<CLightningRestSettings>>(() =>
                Task.Run(() => CreateClnRestSettings(rpc, new Uri(server)))));
            return new CLightningClient(setup.Value.GetAwaiter().GetResult(), Network);
        }

        // Makes a rune over RPC and waits for clnrest to accept requests.
        private static async Task<CLightningRestSettings> CreateClnRestSettings(CLightningClient rpc, Uri server, JArray restrictions = null)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var rune = await rpc.SendCommandAsync<JObject>("createrune", new { restrictions });
                    var settings = new CLightningRestSettings(server) { Rune = rune["rune"].Value<string>() };
                    await new CLightningClient(settings, Network).GetInfoAsync();
                    return settings;
                }
                catch (Exception) when (attempt < 60)
                {
                    await Task.Delay(1000);
                }
            }
        }

        private static EclairLightningClient CreateEclairClient()
        {
            var host = BaseTests.Docker ? "http://eclair:8080" : "http://127.0.0.1:4570";
            return new EclairLightningClient(new Uri(host), "bukkake", Network);
        }

        private static EclairLightningClient CreateEclairClientDest()
        {
            var host = BaseTests.Docker ? "http://eclair_dest:8080" : "http://127.0.0.1:4571";
            return new EclairLightningClient(new Uri(host), "bukkake", Network);
        }

        public static IEnumerable<(string Name, ILightningClient Client)> GetLightningClients()
        {
            yield return ("C-Lightning (Client)", CreateCLightningClient());
            yield return ("C-Lightning (REST)", CreateCLightningRestClient());
            yield return ("LND (Client)", CreateLndClient());
            yield return ("Eclair (Client)", CreateEclairClient());
        }

        public static IEnumerable<(string Name, ILightningClient Customer, ILightningClient Merchant)> GetTestedPairs()
        {
            yield return ("C-Lightning", CreateCLightningClient(), CreateCLightningClientDest());
            yield return ("C-Lightning (REST)", CreateCLightningRestClient(), CreateCLightningRestClientDest());
            yield return ("LND", CreateLndClient(), CreateLndClientDest());
            yield return ("Eclair", CreateEclairClient(), CreateEclairClientDest());
        }
    }
}
