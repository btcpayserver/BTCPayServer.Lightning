using BTCPayServer.Lightning.Charge;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Lightning.LND;
using NBitcoin;
using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Lightning.Tests
{
    public class Tester
    {
        public static NBitcoin.Network Network
        {
            get
            {
                return NBitcoin.Network.RegTest;
            }
        }

        public static RPCClient CreateRPC()
        {
            return new RPCClient("ceiwHEbqWI83:DwubwWsoo3", "127.0.0.1:37393", Network);
        }

        public static ChargeClient CreateChargeClient()
        {
            return new ChargeClient(new Uri("http://api-token:foiewnccewuify@127.0.0.1:37462"), Network);
        }

        public static LndClient CreateLndClient()
        {
            return new LndClient(new LndRestSettings()
            {
                AllowInsecure = true,
                Uri = new Uri("https://127.0.0.1:32736")
            }, Network.RegTest);
        }

        public static CLightningClient CreateCLightningClient()
        {
            return new CLightningClient(new Uri("tcp://127.0.0.1:48532"), Network);
        }

        public static CLightningClient CreateCLightningClientDest()
        {
            return new CLightningClient(new Uri("tcp://127.0.0.1:42549"), Network);
        }

        public static LndClient CreateLndClientDest()
        {
            return new LndClient(new LndRestSettings()
            {
                AllowInsecure = true,
                Uri = new Uri("https://127.0.0.1:42802")
            }, Network.RegTest);
        }

        public static IEnumerable<ILightningClient> GetLightningClients()
        {
            yield return CreateChargeClient();
            yield return CreateCLightningClient();
            yield return CreateLndClient();
        }

        public static IEnumerable<ILightningClient> GetLightningSenderClients()
        {
            yield return CreateCLightningClient();
            yield return CreateLndClient();
        }
        public static IEnumerable<ILightningClient> GetLightningDestClients()
        {
            yield return CreateCLightningClientDest();
            yield return CreateLndClientDest();
        }
    }
}
