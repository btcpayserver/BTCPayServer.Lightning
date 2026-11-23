using System;
using System.Net.Http;
using BTCPayServer.Lightning.LndHub;
using NBitcoin;

namespace BTCPayServer.Lightning.LNDhub;

public class LndHubConnectionStringHandler : ILightningConnectionStringHandler
{
    private readonly HttpClient _httpClient;

    public LndHubConnectionStringHandler(HttpClient httpClient = null)
    {
        _httpClient = httpClient;
    }
    private static bool TryParseLNDhub(string str, out string transformedConnectionString, out string error)
    {
        var parts = str.Replace("lndhub://", "").Split('@');
        if (parts.Length != 2 || !Uri.TryCreate(parts[1].Replace("://", $"://{parts[0]}@"), UriKind.Absolute, out var uri))
        {
            transformedConnectionString = null;
            error = "Invalid LNDhub URI";
            return false;
        }
            
        // transform into connection string format
        transformedConnectionString = $"type=lndhub;server={uri.AbsoluteUri}";
        error = null;
        return true; 
    }

    public ILightningClient Create(string connectionString, Network network, out string error)
    {
        if(connectionString.StartsWith("lndhub://", StringComparison.OrdinalIgnoreCase))
        {
            return !TryParseLNDhub(connectionString, out connectionString, out error) ? null : Create(connectionString, network, out error);
        }
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "lndhub")
        {
            error = null;
            return null;
        }

        if (!kv.TryGetValue("server", out var server))
        {
            error = $"The key 'server' is mandatory for lndhub connection strings";
            return null;
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme != "http" && uri.Scheme != "https")
        {
            error = "The key 'server' should be an URI starting by http:// or https://";
            return null;
        }

        // Legacy TLS options are ignored. Transport security is configured by
        // the caller's HttpClient handler.
        
        var parts = uri.UserInfo.Split(':');
        string username = null;
        string password = null;
        if (!string.IsNullOrEmpty(uri.UserInfo) && parts.Length == 2)
        {
            username = parts[0];
            password = parts[1];
        }
        else
        {

            kv.TryGetValue("username", out username);
            kv.TryGetValue("password", out password);
        }

        error = null;
        return new LndHubLightningClient(uri, username, password, network, _httpClient);
    }
}
