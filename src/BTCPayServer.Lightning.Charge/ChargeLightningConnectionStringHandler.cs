using System;
using System.Linq;
using System.Net.Http;
using NBitcoin;

namespace BTCPayServer.Lightning.Charge;

public class ChargeLightningConnectionStringHandler : ILightningConnectionStringHandler
{
    private readonly HttpClient _httpClient;

    public ChargeLightningConnectionStringHandler(HttpClient httpClient = null)
    {
        _httpClient = httpClient;
    }
    public ILightningClient Create(string connectionString, Network network, out string error)
    {
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "charge")
        {
            error = null;
            return null;
        }
        
        if (!kv.TryGetValue("server", out var server))
        {
            error = $"The key 'server' is mandatory for charge connection strings";
            return null;
        }


        bool allowInsecure = false;
        if ( kv.TryGetValue("allowinsecure", out var allowinsecureStr))
        {
            var allowedValues = new[] {"true", "false"};
            if (!allowedValues.Any(v => v.Equals(allowinsecureStr, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"The key 'allowinsecure' should be true or false";
                return null;
            }

            allowInsecure = allowinsecureStr.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            error = $"The key 'server' should be an URI starting by http:// or https://";
            return null;
        }

        if (!allowInsecure && uri.Scheme == "http")
        {
            error = $"The key 'allowinsecure' is false, but server's Uri is not using https";
            return null;
        }

        var parts = uri.UserInfo.Split(':');
        string cookieFilePath;
        string username;
        string password = null;
        if (!string.IsNullOrEmpty(uri.UserInfo) && parts.Length == 2)
        {
            username = parts[0];
            password = parts[1];
            if (kv.TryGetValue("cookiefilepath", out cookieFilePath))
            {
                error = "The key 'cookiefilepath' should not be used if you are passing credentials inside the url";
                return null;
            }
        }
        else
        {
            if (kv.TryGetValue("api-token", out var  apiToken) & kv.TryGetValue("cookiefilepath", out cookieFilePath))
            {
                error = "Keys 'api-token' and 'cookiefilepath' are mutually exclusive";
                return null;
            }

            if (apiToken != null)
            {
                username = "api-token";
                password = apiToken;
            }
            else if (cookieFilePath != null)
            {
                username = "api-token";
            }
            else
            {
                if (!kv.TryGetValue("username", out username) || !kv.TryGetValue("password", out password))
                {
                    error = "The key 'api-token' or 'cookiefilepath' or ('username' and 'password') is not found";
                    return null;
                }
            }
        }

        var baseUri = new UriBuilder(uri) {UserName = username??"", Password = password??""}.Uri;
        error = null;
        if (cookieFilePath != null)
        {
            return new ChargeClient(baseUri, cookieFilePath, network, _httpClient, allowInsecure);
        }
        return new ChargeClient(baseUri, network, _httpClient, allowInsecure);
    }
}
