using System;
using System.Net.Http;
using NBitcoin;

namespace BTCPayServer.Lightning.Phoenixd;

public class PhoenixdConnectionStringHandler : ILightningConnectionStringHandler
{
    private readonly HttpClient _httpClient;

    public PhoenixdConnectionStringHandler(HttpClient  httpClient = null)
    {
        _httpClient = httpClient;
    }
    public ILightningClient Create(string connectionString, Network network, out string error)
    {
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "phoenixd")
        {
            error = null;
            return null;
        }

        if (!kv.TryGetValue("server", out var server))
        {
            error = $"The key 'server' is mandatory for Phoenixd connection strings";
            return null;
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out var Phoenixduri)
            || (Phoenixduri.Scheme != "http" && Phoenixduri.Scheme != "https"))
        {
            error = $"The key 'server' should be an URI starting by http:// or https://";
            return null;
        }

        kv.TryGetValue("username", out var username);
        kv.TryGetValue("password", out var password);

        error = null;
        return new PhoenixdLightningClient(Phoenixduri, username, password, network, _httpClient);
    }
}
