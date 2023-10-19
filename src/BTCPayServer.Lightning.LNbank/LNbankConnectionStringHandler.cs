using System;
using System.Linq;
using System.Net.Http;
using NBitcoin;

namespace BTCPayServer.Lightning.LNbank;

public class LNbankConnectionStringHandler : ILightningConnectionStringHandler
{
    private readonly HttpClient _httpClient;

    public LNbankConnectionStringHandler(HttpClient httpClient = null)
    {
        _httpClient = httpClient;
    }

    public ILightningClient Create(string connectionString, Network network, out string error)
    {
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "lnbank")
        {
            error = null;
            return null;
        }

        if (!kv.TryGetValue("server", out var server))
        {
            error = $"The key 'server' is mandatory for LNbank connection strings";
            return null;
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri)
            || uri.Scheme != "http" && uri.Scheme != "https")
        {
            error = "The key 'server' should be an URI starting by http:// or https://";
            return null;
        }


        bool allowInsecure = false;
        if (kv.TryGetValue("allowinsecure", out var allowinsecureStr))
        {
            var allowedValues = new[] {"true", "false"};
            if (!allowedValues.Any(v => v.Equals(allowinsecureStr, StringComparison.OrdinalIgnoreCase)))
            {
                error = "The key 'allowinsecure' should be true or false";
                return null;
            }

            allowInsecure = allowinsecureStr.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        if (!LightningConnectionStringHelper.VerifySecureEndpoint(uri, allowInsecure))
        {
            error = "The key 'allowinsecure' is false, but server's Uri is not using https";
            return null;
        }

        if (!kv.TryGetValue("api-token", out var apiToken))
        {
            error = "The key 'api-token' is not found";
            return null;
        }

        error = null;
        return new LNbankLightningClient(uri, apiToken, network, _httpClient);
    }
}
