using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Charge;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Lightning.Eclair;
using BTCPayServer.Lightning.LNbank;
using BTCPayServer.Lightning.LND;
using BTCPayServer.Lightning.LndHub;
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
            return CreateClient(conn, network);
        }

        public LightningClientFactory(Network network)
        {
            Network = network ?? throw new ArgumentNullException(nameof(network));
        }

        public Network Network { get; }
        public HttpClient HttpClient { get; set; }

        public ILightningClient Create(string connectionString) => CreateClient(connectionString, Network);

        public ILightningClient Create(LightningConnectionString connectionString)
        {
            if (connectionString == null)
                throw new ArgumentNullException(nameof(connectionString));
            if (connectionString.ConnectionType == LightningConnectionType.Charge)
            {
                if (connectionString.CookieFilePath != null)
                {
                    return new ChargeClient(connectionString.BaseUri, connectionString.CookieFilePath, Network,
                        HttpClient, connectionString.AllowInsecure);
                }
                return new ChargeClient(connectionString.ToUri(true), Network, HttpClient, connectionString.AllowInsecure);
            }

            if (connectionString.ConnectionType == LightningConnectionType.CLightning)
            {
                return new CLightningClient(connectionString.ToUri(false), Network);
            }

            if (connectionString.ConnectionType == LightningConnectionType.LndREST)
            {
                return new LndClient(new LndSwaggerClient(new LndRestSettings(connectionString.BaseUri)
                {
                    Macaroon = connectionString.Macaroon,
                    MacaroonFilePath = connectionString.MacaroonFilePath,
                    CertificateThumbprint = connectionString.CertificateThumbprint,
                    AllowInsecure = connectionString.AllowInsecure,
                }, HttpClient), Network);
            }

            if (connectionString.ConnectionType == LightningConnectionType.Eclair)
            {
                return new EclairLightningClient(connectionString.BaseUri, connectionString.Username, connectionString.Password, Network, HttpClient);
            }

            if (connectionString.ConnectionType == LightningConnectionType.LNbank)
            {
                return new LNbankLightningClient(connectionString.BaseUri, connectionString.ApiToken, Network, HttpClient);
            }

            if (connectionString.ConnectionType == LightningConnectionType.LNDhub)
            {
                return new LndHubLightningClient(connectionString.BaseUri, connectionString.Username, connectionString.Password, Network, HttpClient);
            }

            throw new NotSupportedException(
                $"Unsupported connection string for lightning server ({connectionString.ConnectionType})");
        }
    }
}
