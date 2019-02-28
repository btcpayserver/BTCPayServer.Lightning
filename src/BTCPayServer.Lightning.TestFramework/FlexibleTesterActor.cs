using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NBitcoin;
using NBitcoin.RPC;

namespace BTCPayServer.Lightning.TestFramework
{

    public enum SupportedActorType
    {
        Lnd,
        Lightning,
        LightningCharge,
        LndWithBitcoin,

        LightningWithBitcoin,
        LightningChargeWithbitcoin,
    }

    public abstract class FlexibleTesterActorBase
    {

        public Network Network { get; set; }

        public int Port { get; set; }

        public string ID { get; set; }

        protected ILightningClient _Client { get; set; }

        public abstract string TypeToString();

        private ILightningClientFactory Factory { get; }
        public FlexibleTesterActorBase(Network network)
        {
            Network = network;
            Factory = new LightningClientFactory(network);
            ID = String.Concat(
                System.Guid.NewGuid()
                    .ToString("N")
                    // If the ID starts from digit, that will be invalid for docker-compose interpolation.
                    .SkipWhile(c => char.IsDigit(c))
                );

            // ports
            var ports = new int[1];
            TestUtils.FindPorts(ports);
            Port = ports[0];
        }

        public ILightningClient GetClient()
        {
            if (_Client == null)
                _Client = Factory.Create(GetConnectionString());
            return _Client;
        }

        public RPCClient GetBitcoinClient()
            => new RPCClient("ceiwHEbqWI83:DwubwWsoo3", "127.0.0.1:37393", Network);

        public abstract string GetConnectionString();

        public abstract ProcessStartInfo UpdateEnvironmentVariables(ProcessStartInfo pInfo);

        public string GetFragmentFileName() => 
            TypeToString() + ".yml";
    }

    public class LndActor : FlexibleTesterActorBase
    {
        public LndActor(Network network) : base(network){}
        public override string TypeToString()
            => "lnd";
        public override string GetConnectionString()
            => $"type=lnd-rest;server=https://lnd:lnd@127.0.0.1:{Port};allowinsecure=true";
        public override ProcessStartInfo UpdateEnvironmentVariables(ProcessStartInfo pInfo)
        {
            pInfo.EnvironmentVariables.Add($"{ID}_PORT", Port.ToString());
            return pInfo;
        }
    }

    public class CLightningActor : FlexibleTesterActorBase
    {
        public CLightningActor(Network network) : base(network){}

        public override string TypeToString()
            => "lightningd";
        public override string GetConnectionString()
            => $"type=clightning;server=tcp://127.0.0.1:{Port}";
        public override ProcessStartInfo UpdateEnvironmentVariables(ProcessStartInfo pInfo)
        {
            pInfo.EnvironmentVariables.Add($"{ID}_PORT", Port.ToString());
            return pInfo;
        }
    }

    public class LightningChargeActor : FlexibleTesterActorBase
    {
        private int LightningPort { get; }

        public LightningChargeActor(Network network) : base(network)
        {
            var additionalPort = new int[1];
            TestUtils.FindPorts(additionalPort);
            LightningPort = additionalPort[0];
        }

        public override string TypeToString()
            => "lightning-charge";

        public override string GetConnectionString()
            => $"type=charge;server=http://api-token:foiewnccewuify@127.0.0.1:{Port}";
        public override ProcessStartInfo UpdateEnvironmentVariables(ProcessStartInfo pInfo)
        {
            pInfo.EnvironmentVariables.Add($"{ID}_CHARGE_PORT", Port.ToString());
            pInfo.EnvironmentVariables.Add($"{ID}_LIGHTNING_PORT", LightningPort.ToString());
            return pInfo;
        }
    }

    public class FlexibleTesterActorFactory
    {
        public FlexibleTesterActorFactory(Network network) => Network = network;

        public Network Network { get; }

        public FlexibleTesterActorBase Create(SupportedActorType type)
        {
            if (type == SupportedActorType.Lnd)
                return new LndActor(Network);
            if (type == SupportedActorType.Lightning)
                return new CLightningActor(Network);
            if (type == SupportedActorType.LightningCharge)
                return new LightningChargeActor(Network);

            throw new NotSupportedException($"Unknown actor type{type}");
        }
    }
}