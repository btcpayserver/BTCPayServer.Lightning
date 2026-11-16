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

    /// <param name="httpClient">Used for clnrest unless a pinned certificate or allowinsecure on https needs a handler of its own.
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
            if (!ClnRestTransport.IsRootUrl(uri))
            {
                error = "The key 'server' should be the root URL of clnrest, without a path, query or fragment";
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

            // The file is sent as a header, so at least make sure it looks like a rune.
            // This doesn't make the key safe for untrusted users.
            if (!runeFilePath.EndsWith(".rune", StringComparison.OrdinalIgnoreCase))
            {
                error = "The key 'runefilepath' should point to a .rune file";
                return null;
            }
        }
        else if (rune == null)
        {
            error = "The key 'rune' or 'runefilepath' is mandatory for clnrest";
            return null;
        }

        byte[] certificateThumbprint = null;
        if (kv.TryGetValue("certthumbprint", out var certthumbprint))
        {
            try
            {
                certificateThumbprint = ConvertHelper.FromHexString(certthumbprint.Replace(":", string.Empty));
            }
            catch
            {
                error = "The key 'certthumbprint' has invalid format: it should be the SHA256 of the certificate";
                return null;
            }

            if (certificateThumbprint.Length != 32)
            {
                error = "The key 'certthumbprint' has invalid length: it should be the SHA256 of the certificate (32 bytes)";
                return null;
            }
        }

        if (kv.TryGetValue("certfilepath", out var certificateFilePath) && certificateThumbprint != null)
        {
            error = "The key 'certfilepath' conflict with 'certthumbprint'";
            return null;
        }

        if (uri.Scheme != "https" && (certificateThumbprint != null || certificateFilePath != null))
        {
            error = "The keys 'certthumbprint' and 'certfilepath' need an https:// server";
            return null;
        }

        var allowInsecure = false;
        if (kv.TryGetValue("allowinsecure", out var allowInsecureStr) && !bool.TryParse(allowInsecureStr, out allowInsecure))
        {
            error = "The key 'allowinsecure' should be true or false";
            return null;
        }

        if (!LightningConnectionStringHelper.VerifySecureEndpoint(uri, allowInsecure))
        {
            error = "The key 'allowinsecure' is false, but server's Uri is not using https";
            return null;
        }

        error = null;
        return new CLightningClient(new CLightningRestSettings(uri)
        {
            Rune = rune,
            RuneFilePath = runeFilePath,
            CertificateThumbprint = certificateThumbprint,
            CertificateFilePath = certificateFilePath,
            AllowInsecure = allowInsecure
        }, network, _httpClient);
    }
}
