using System;
using System.Collections.Generic;
using System.Net.Http;
using NBitcoin;

namespace BTCPayServer.Lightning.CLightning;

public class CLightningConnectionStringHandler : ILightningConnectionStringHandler
{
    private readonly HttpClient _httpClient;

    public CLightningConnectionStringHandler()
    {
    }

    /// <param name="httpClient">Used for all clnrest requests, including caller-configured TLS validation and proxies.
    /// It must not follow redirects, or it sends the rune to the redirect target, and its timeout must be at least 2 seconds.</param>
    public CLightningConnectionStringHandler(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public ILightningClient Create(string connectionString, Network network, out string error)
    {



        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "clightning")
        {
            error = null;
            return null;
        }

        if (!kv.TryGetValue("server", out var server))
        {
            error = $"The key 'server' is mandatory for clightning connection strings";
            return null;
        }


        if (server.StartsWith("//", StringComparison.OrdinalIgnoreCase))
            server = "unix:" + server;
        else if (server.StartsWith("/", StringComparison.OrdinalIgnoreCase))
            server = "unix:/" + server;

        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri)
            || (uri.Scheme != "tcp" && uri.Scheme != "unix" && uri.Scheme != "http" && uri.Scheme != "https"))
        {
            error = $"The key 'server' should be an URI starting by tcp://, unix://, http:// (clnrest) or https:// (clnrest) or a path to the 'lightning-rpc' unix socket";
            return null;
        }

        if (uri.Scheme == "http" || uri.Scheme == "https")
        {
            if (!ClnRestTransport.IsBaseUrl(uri))
            {
                error = "The key 'server' should be the base URL of clnrest, without a query or fragment";
                return null;
            }
            return CreateRestClient(uri, kv, network, out error);
        }

        if (kv.ContainsKey("rune") || kv.ContainsKey("runefilepath"))
        {
            error = "The keys 'rune' and 'runefilepath' need a clnrest server starting by http:// or https://";
            return null;
        }

        error = null;
        return new CLightningClient(uri, network);
    }

    private ILightningClient CreateRestClient(Uri uri, Dictionary<string, string> kv, Network network, out string error)
    {
        kv.TryGetValue("rune", out var rune);
        if (kv.TryGetValue("runefilepath", out var runeFilePath))
        {
            if (rune != null)
            {
                error = "The key 'rune' is already specified";
                return null;
            }
        }
        else if (rune == null)
        {
            error = "The key 'rune' or 'runefilepath' is mandatory for clnrest";
            return null;
        }

        if (rune != null && !ClnRestTransport.IsRuneFormatValid(rune))
        {
            error = "The key 'rune' has invalid format";
            return null;
        }

        // Legacy TLS options are ignored. TLS validation belongs to the
        // caller's HttpClient handler, not to the connection string.

        error = null;
        return new CLightningClient(new CLightningRestSettings(uri)
        {
            Rune = rune,
            RuneFilePath = runeFilePath
        }, network, _httpClient);
    }
}
