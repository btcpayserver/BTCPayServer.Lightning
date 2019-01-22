using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Charge;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Lightning.Eclair;
using BTCPayServer.Lightning.LND;
using NBitcoin;
using NBitcoin.RPC;

namespace BTCPayServer.Lightning
{
    public class LightningClientFactory : ILightningClientFactory
    {
        public static ILightningClient CreateClient(LightningConnectionString connString, Network network)
        {
            switch (connString.ConnectionType)
            {
                case LightningConnectionType.Charge when connString.CookieFilePath != null:
                    return new ChargeClient(connString.BaseUri, connString.CookieFilePath, network);
                case LightningConnectionType.Charge:
                    return new ChargeClient(connString.ToUri(true), network);
                case LightningConnectionType.CLightning:
                    return new CLightningClient(connString.ToUri(false), network);
                case LightningConnectionType.LndREST:
                    return new LndClient(new LndSwaggerClient(new LndRestSettings(connString.BaseUri)
                    {
                        Macaroon = connString.Macaroon,
                        MacaroonFilePath = connString.MacaroonFilePath,
                        CertificateThumbprint = connString.CertificateThumbprint,
                        AllowInsecure = connString.AllowInsecure,
                    }), network);
                case LightningConnectionType.Eclair:
                    return new EclairLightningClient(connString.BaseUri,connString.Password,network,new RPCClient(connString.BitcoinAuth, connString.BitcoinHost, network));
                default:
                    throw new NotSupportedException($"Unsupported connection string for lightning server ({connString.ConnectionType})");
            }
        }

        public static ILightningClient CreateClient(string connectionString, Network network)
        {
            if(!LightningConnectionString.TryParse(connectionString, false, out var conn, out string error))
                throw new FormatException($"Invalid format ({error})");
            return LightningClientFactory.CreateClient(conn, network);
        }

        public LightningClientFactory(Network network)
        {
            if(network == null)
                throw new ArgumentNullException(nameof(network));
            Network = network;
        }

        public Network Network
        {
            get;
        }

        public ILightningClient Create(string connectionString)
        {
            return LightningClientFactory.CreateClient(connectionString, Network);
        }
        public ILightningClient Create(LightningConnectionString connectionString)
        {
            if(connectionString == null)
                throw new ArgumentNullException(nameof(connectionString));
            return LightningClientFactory.CreateClient(connectionString, Network);
        }
    }
}
