using System;
using System.Net.Http;
using NBitcoin;

namespace BTCPayServer.Lightning.LND;

public class LndConnectionStringHandler : ILightningConnectionStringHandler
{
    private readonly HttpClient _httpClient;

    public LndConnectionStringHandler(HttpClient httpClient = null)
    {
        _httpClient = httpClient;
    }

    public ILightningClient Create(string connectionString, Network network, out string error)
    {
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "lnd-rest" && type != "lnd-grpc")
        {
            error = null;
            return null;
        }

        if (!kv.TryGetValue("server", out var server))
        {
            error = $"The key 'server' is mandatory for lnd connection strings";
            return null;
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri)
            || uri.Scheme != "http" && uri.Scheme != "https")
        {
            error = "The key 'server' should be an URI starting by http:// or https://";
            return null;
        }

        byte[] macaroonData = null;

        string username = null;
        string password = null;
        var parts = uri.UserInfo.Split(':');
        if (!string.IsNullOrEmpty(uri.UserInfo) && parts.Length == 2)
        {
            username = parts[0];
            password = parts[1];
        }

        // uri = new UriBuilder(uri) {UserName = "", Password = ""}.Uri;

        if (kv.TryGetValue("macaroon", out var macaroon))
        {
            try
            {
                macaroonData = ConvertHelper.FromHexString(macaroon);
            }
            catch
            {
                error = $"The key 'macaroon' format should be in hex";
                return null;
            }
        }

        kv.TryGetValue("macaroondirectorypath", out var macaroonDirectoryPath);
        if (kv.TryGetValue("macaroonfilepath", out var macaroonFilePath))
        {
            if (macaroon != null)
            {
                error = $"The key 'macaroon' is already specified";
                return null;
            }

            if (!macaroonFilePath.EndsWith(".macaroon", StringComparison.OrdinalIgnoreCase))
            {
                error = $"The key 'macaroonfilepath' should point to a .macaroon file";
                return null;
            }
            
        }


        // Legacy TLS options are intentionally ignored. TLS configuration belongs
        // to the caller's HttpClient handler, not to the connection string.

        error = null;
        return new LndClient(new LndSwaggerClient(new LndRestSettings(uri)
        {
            Macaroon = macaroonData,
            MacaroonFilePath = macaroonFilePath,
            MacaroonDirectoryPath = macaroonDirectoryPath,
        }, _httpClient), network);
    }
}
