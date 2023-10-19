using System;
using System.Linq;
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
        byte[] certificateThumbprint = null;
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


        string securitySet = null;
        if (kv.TryGetValue("certthumbprint", out var certthumbprint))
        {
            try
            {
                var bytes = ConvertHelper.FromHexString(certthumbprint.Replace(":", string.Empty));
                if (bytes.Length != 32)
                {
                    error =
                        $"The key 'certthumbprint' has invalid length: it should be the SHA256 of the PEM format of the certificate (32 bytes)";
                    return null;
                }

                certificateThumbprint = bytes;
            }
            catch
            {
                error =
                    $"The key 'certthumbprint' has invalid format: it should be the SHA256 of the PEM format of the certificate";
                return null;
            }

            securitySet = "certthumbprint";
        }

        if (kv.TryGetValue("certfilepath", out var certificateFilePath))
        {
            if (securitySet != null)
            {
                error = $"The key 'certfilepath' conflict with '{securitySet}'";
                return null;
            }

            securitySet = "certfilepath";
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

        error = null;
        return new LndClient(new LndSwaggerClient(new LndRestSettings(uri)
        {
            Macaroon = macaroonData,
            MacaroonFilePath = macaroonFilePath,
            MacaroonDirectoryPath = macaroonDirectoryPath,
            CertificateThumbprint = certificateThumbprint,
            CertificateFilePath = certificateFilePath,
            AllowInsecure = allowInsecure,
            
        }, _httpClient), network);
    }
}
