using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Charge;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Lightning.Eclair;
using BTCPayServer.Lightning.LND;
using BTCPayServer.Lightning.Ptarmigan;
using NBitcoin;
using NBitcoin.RPC;

namespace BTCPayServer.Lightning
{
    public class LightningClientFactory : ILightningClientFactory
    {
        public static ILightningClient CreateClient(LightningConnectionString connectionString, Network network)
        {
            return new LightningClientFactory(network).Create(connectionString);
        }

        public static ILightningClient CreateClient(string connectionString, Network network)
        {
            if (!LightningConnectionString.TryParse(connectionString, false, out var conn, out string error))
                throw new FormatException($"Invalid format ({error})");
            return LightningClientFactory.CreateClient(conn, network);
        }

        public LightningClientFactory(Network network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            Network = network;
        }

        public Network Network { get; }
        public HttpClient HttpClient { get; set; }

        public ILightningClient Create(string connectionString)
        {
            return LightningClientFactory.CreateClient(connectionString, Network);
        }

        public ILightningClient Create(LightningConnectionString connectionString)
        {
            if (connectionString == null)
                throw new ArgumentNullException(nameof(connectionString));
            if (connectionString.ConnectionType == LightningConnectionType.Charge)
            {
                if (connectionString.CookieFilePath != null)
                {
                    return new ChargeClient(connectionString.BaseUri, connectionString.CookieFilePath, Network,
                        HttpClient);
                }
                else
                {
                    return new ChargeClient(connectionString.ToUri(true), Network, HttpClient);
                }
            }
            else if (connectionString.ConnectionType == LightningConnectionType.CLightning)
            {
                return new CLightningClient(connectionString.ToUri(false), Network);
            }
            else if (connectionString.ConnectionType == LightningConnectionType.LndREST)
            {
                return new LndClient(new LndSwaggerClient(new LndRestSettings(connectionString.BaseUri)
                {
                    Macaroon = connectionString.Macaroon,
                    MacaroonFilePath = connectionString.MacaroonFilePath,
                    CertificateThumbprint = connectionString.CertificateThumbprint,
                    AllowInsecure = connectionString.AllowInsecure,
                }, HttpClient), Network);
            }
            else if (connectionString.ConnectionType == LightningConnectionType.Eclair)
            {
                var rpcClient =
                    !string.IsNullOrEmpty(connectionString.BitcoinHost) &&
                    !string.IsNullOrEmpty(connectionString.BitcoinAuth)
                        ? new RPCClient(connectionString.BitcoinAuth, connectionString.BitcoinHost, Network)
                        {
                            HttpClient = HttpClient
                        }
                        : null;
                return new EclairLightningClient(connectionString.BaseUri, connectionString.Password, Network, rpcClient
                    , HttpClient);
            }
            else if (connectionString.ConnectionType == LightningConnectionType.Ptarmigan)
            {
                var rpcClient =
                    !string.IsNullOrEmpty(connectionString.BitcoinHost) &&
                    !string.IsNullOrEmpty(connectionString.BitcoinAuth)
                        ? new RPCClient(connectionString.BitcoinAuth, connectionString.BitcoinHost, Network)
                        {
                            HttpClient = HttpClient
                        }
                        : null;
                return new PtarmiganLightningClient(connectionString.BaseUri, connectionString.ApiToken, Network, rpcClient, HttpClient);
            }
            else
                throw new NotSupportedException(
                    $"Unsupported connection string for lightning server ({connectionString.ConnectionType})");
        }
    }
}