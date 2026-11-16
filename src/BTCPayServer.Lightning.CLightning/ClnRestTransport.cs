using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.CLightning;

// Calls lightningd through the clnrest plugin. Each call is a POST to /v1/<method>
// with the named parameters as a JSON object and the rune in the 'rune' header.
class ClnRestTransport
{
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false });

    private readonly HttpClient _httpClient;

    public ClnRestTransport(CLightningRestSettings settings, HttpClient httpClient)
    {
        if (settings.Server == null)
            throw new ArgumentException("The server is mandatory", nameof(settings));
        if (!IsRootUrl(settings.Server))
            throw new ArgumentException("The server should be the root URL of clnrest, without a path, query or fragment", nameof(settings));
        if ((settings.Rune == null) == (settings.RuneFilePath == null))
            throw new ArgumentException("Exactly one of Rune and RuneFilePath should be set", nameof(settings));
        if (!LightningConnectionStringHelper.VerifySecureEndpoint(settings.Server, settings.AllowInsecure))
            throw new InvalidOperationException("AllowInsecure is set to false, but the URI is not using https");
        if (settings.Server.Scheme != "https" && (settings.CertificateThumbprint != null || settings.CertificateFilePath != null))
            throw new ArgumentException("A pinned certificate needs an https server", nameof(settings));
        Settings = settings;
        _httpClient = CreateHttpClient(settings, httpClient);
        if (_httpClient.Timeout != Timeout.InfiniteTimeSpan && _httpClient.Timeout < MinHttpClientTimeout)
            throw new ArgumentException($"The HttpClient timeout should be at least {MinHttpClientTimeout.TotalSeconds} seconds for clnrest", nameof(httpClient));
    }

    public CLightningRestSettings Settings { get; }

    // clnrest has no request timeout, but HttpClient has one (100 seconds by default).
    // waitanyinvoice gives up after 30 seconds, or before HttpClient does: 5 seconds before
    // when that leaves at least 5 seconds, else halfway.
    internal int WaitAnyInvoiceTimeoutSeconds
    {
        get
        {
            var limit = _httpClient.Timeout;
            if (limit == Timeout.InfiniteTimeSpan || limit >= TimeSpan.FromSeconds(35))
                return 30;
            if (limit >= TimeSpan.FromSeconds(10))
                return (int)(limit.TotalSeconds - 5);
            return Math.Max(1, (int)(limit.TotalSeconds / 2));
        }
    }

    // A wait of 1 second still has to end before the HttpClient gives up.
    internal static readonly TimeSpan MinHttpClientTimeout = TimeSpan.FromSeconds(2);

    // clnrest serves /v1/<method> at the root. Without a path, query or fragment on the server,
    // the request can't reach any other path.
    internal static bool IsRootUrl(Uri server)
    {
        return server.AbsoluteUri == server.GetLeftPart(UriPartial.Authority) + "/";
    }

    // Response text can come from any service the server URL points at, so error messages
    // only keep a short lightningd message.
    internal const int MaxErrorMessageLength = 200;

    public async Task<JToken> CallAsync(string method, JObject parameters, Action onSending, CancellationToken cancellation)
    {
        // Everything that can fail locally, like reading the rune file, happens before onSending.
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Settings.Server, "v1/" + method));
        request.Content = new StringContent(parameters.ToString(Formatting.None), Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("rune", GetRune());
        onSending?.Invoke();
        var (status, body) = await SendAsync(request, cancellation);
        var json = TryParseJson(body);
        if (json == null)
            throw new HttpRequestException($"clnrest answered HTTP {status} with a body that isn't JSON");
        if (status >= 200 && status <= 299)
            return json;
        throw ToException(json, status);
    }

    private async Task<(int Status, string Body)> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        // HttpClient's timeout stops at the headers when the body is streamed, so the whole call gets the same limit.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        if (_httpClient.Timeout != Timeout.InfiniteTimeSpan)
            timeout.CancelAfter(_httpClient.Timeout);
        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            // .NET Framework checks the token only before a read starts, so a stalled body is cut
            // by disposing the response, and the read fails with ObjectDisposedException or IOException.
            using (timeout.Token.Register(response.Dispose))
            {
                try
                {
                    return ((int)response.StatusCode, await ReadBodyAsync(response.Content, timeout.Token));
                }
                catch (Exception e) when (e is ObjectDisposedException or IOException && timeout.IsCancellationRequested)
                {
                    throw new OperationCanceledException(timeout.Token);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Report the caller's cancellation like the socket transport does.
            throw new OperationCanceledException(cancellation);
        }
    }

    // CLN keeps paid invoices unless autoclean removes them, and listinvoices returns them all.
    // At 0.6 to 0.8 KB per paid invoice (the examples in doc/schemas/listinvoices.json at v26.06.10),
    // 128 MB holds about 160k of them. The cap keeps a broken or hostile server from filling memory.
    internal const int MaxResponseSize = 128 * 1024 * 1024;

    private static async Task<string> ReadBodyAsync(HttpContent content, CancellationToken cancellation)
    {
        if (content.Headers.ContentLength > MaxResponseSize)
            throw new HttpRequestException($"clnrest answered with more than {MaxResponseSize} bytes");
        using var stream = await content.ReadAsStreamAsync();
        using var body = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation)) > 0)
        {
            if (body.Length + read > MaxResponseSize)
                throw new HttpRequestException($"clnrest answered with more than {MaxResponseSize} bytes");
            body.Write(buffer, 0, read);
        }
        body.Position = 0;
        using var reader = new StreamReader(body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    // Only a lightningd error code tells what happened on the node. Anything else (no code,
    // a proxy error page, clnrest unable to reach lightningd) leaves the outcome unknown, so it
    // must not become a LightningRPCException that a caller could read as a failed payment.
    private static Exception ToException(JToken json, int statusCode)
    {
        if (!CLightningClient.TryParseRpcError(json, out var rpcError))
            return new HttpRequestException($"clnrest answered HTTP {statusCode} without a lightningd error code");
        if (rpcError.Message.Length <= MaxErrorMessageLength)
            return rpcError;
        return new LightningRPCException(rpcError.Message.Substring(0, MaxErrorMessageLength) + "...", rpcError.Code);
    }

    private static JToken TryParseJson(string body)
    {
        try
        {
            return JToken.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string GetRune()
    {
        if (Settings.Rune != null)
            return Settings.Rune;
        var rune = File.ReadAllText(Settings.RuneFilePath).Trim();
        if (rune.Length == 0)
            throw new InvalidOperationException($"The rune file '{Settings.RuneFilePath}' is empty");
        return rune;
    }

    internal static HttpClient CreateHttpClient(CLightningRestSettings settings, HttpClient defaultHttpClient)
    {
        // Like lnd-rest, a pinned certificate or allowinsecure on https needs a handler of our own.
        // Otherwise the caller's client, with its proxy and SSRF settings, does the job.
        var pinned = settings.CertificateThumbprint != null || settings.CertificateFilePath != null;
        if (settings.Server.Scheme != "https" || (!pinned && !settings.AllowInsecure))
            return defaultHttpClient ?? SharedClient;
        return new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = CreateCertificateValidator(settings)
        });
    }

    // clnrest serves its server.pem without the CA that signed it, so the pin is on that leaf.
    // A pin replaces the name and chain checks.
    private static Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> CreateCertificateValidator(CLightningRestSettings settings)
    {
        var expected = settings.CertificateThumbprint != null
            ? new[] { settings.CertificateThumbprint.ToArray() }
            : settings.CertificateFilePath != null
                ? ReadCertificateHashes(settings.CertificateFilePath)
                : null;
        if (expected == null)
            return (_, _, _, _) => true;
        return (_, cert, _, _) => cert != null && expected.Any(hash => hash.SequenceEqual(GetHash(cert.RawData)));
    }

    // A PEM certificate is about 1 KB. The cap keeps a path like /dev/zero from filling memory.
    internal const int MaxCertificateFileSize = 64 * 1024;

    // Read once, when the client is created, and not on each TLS handshake.
    private static byte[][] ReadCertificateHashes(string path)
    {
#if NO_PEM_IMPORT
        throw new NotSupportedException("CertificateFilePath is supported only from .NET6.0");
#else
        var buffer = new byte[MaxCertificateFileSize + 1];
        var length = 0;
        using (var file = File.OpenRead(path))
        {
            int read;
            while (length < buffer.Length && (read = file.Read(buffer, length, buffer.Length - length)) > 0)
                length += read;
        }
        if (length > MaxCertificateFileSize)
            throw new InvalidOperationException($"The certificate file '{path}' is bigger than {MaxCertificateFileSize} bytes");
        var certificates = new X509Certificate2Collection();
        certificates.ImportFromPem(Encoding.ASCII.GetString(buffer, 0, length));
        if (certificates.Count == 0)
            throw new InvalidOperationException($"The certificate file '{path}' has no certificate");
        return certificates.Cast<X509Certificate2>().Select(certificate => GetHash(certificate.RawData)).ToArray();
#endif
    }

    private static byte[] GetHash(byte[] data)
    {
        using var alg = SHA256.Create();
        return alg.ComputeHash(data);
    }
}
