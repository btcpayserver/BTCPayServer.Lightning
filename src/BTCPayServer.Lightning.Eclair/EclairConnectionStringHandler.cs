using System;
using System.Net.Http;
using NBitcoin;

namespace BTCPayServer.Lightning.Eclair;

public class EclairConnectionStringHandler : ILightningConnectionStringHandler
{
    private readonly HttpClient _httpClient;

    public EclairConnectionStringHandler(HttpClient  httpClient = null)
    {
        _httpClient = httpClient;
    }
    public ILightningClient Create(string connectionString, Network network, out string error)
    {
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "eclair")
        {
            error = null;
            return null;
        }
        
        if (!kv.TryGetValue("server", out var server))
        {
            error = $"The key 'server' is mandatory for eclair connection strings";
            return null;
        }
        
        if (!Uri.TryCreate(server, UriKind.Absolute, out var eclairuri)
            || (eclairuri.Scheme != "http" && eclairuri.Scheme != "https"))
        {
            error = $"The key 'server' should be an URI starting by http:// or https://";
            return null;
        }

        kv.TryGetValue("username", out var username);
        kv.TryGetValue("password", out var password);
        if (kv.TryGetValue("bitcoin-host", out var bitcoinHost))
        {
            if (!kv.TryGetValue("bitcoin-auth", out var bitcoinAuth))
            {
                
                error =
                    $"The key 'bitcoin-auth' is mandatory for eclair connection strings when bitcoin-host is specified";
                return null;
            }
        }

        error = null;
        return new EclairLightningClient(eclairuri,  username, password, network, _httpClient);
    }
}
