using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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
    private readonly SemaphoreSlim _runeLock = new(1, 1);
    private string _cachedRune;

    public ClnRestTransport(CLightningRestSettings settings, HttpClient httpClient)
    {
        if (settings.Server == null)
            throw new ArgumentException("The server is mandatory", nameof(settings));
        if (!IsRootUrl(settings.Server))
            throw new ArgumentException("The server should be the root URL of clnrest, without a path, query or fragment", nameof(settings));
        if ((settings.Rune == null) == (settings.RuneFilePath == null))
            throw new ArgumentException("Exactly one of Rune and RuneFilePath should be set", nameof(settings));
        if (settings.Server.Scheme != "http" && settings.Server.Scheme != "https")
            throw new ArgumentException("The server should use http:// or https://", nameof(settings));
        Settings = settings;
        _httpClient = httpClient ?? SharedClient;
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
        cancellation.ThrowIfCancellationRequested();
        // Initial credential loading and validation happen before onSending.
        var rune = await GetRuneAsync(cancellation);
        for (var attempt = 0; ; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Settings.Server, "v1/" + method));
            request.Content = new StringContent(parameters.ToString(Formatting.None), Encoding.UTF8, "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("rune", rune);
            onSending?.Invoke();
            var (status, body) = await SendAsync(request, cancellation);
            var json = TryParseJson(body);
            if (json == null)
                throw new HttpRequestException($"clnrest answered HTTP {status} with a body that isn't JSON");
            if (status >= 200 && status <= 299)
                return json;
            var error = ToException(json, status);
            // These explicit CLN errors reject authorization before executing the
            // command. Never replay a possibly executed payment on transport errors.
            if (attempt == 0 && Settings.RuneFilePath != null &&
                error is LightningRPCException rpcError &&
                (rpcError.Code == CLightningErrorCode.RUNE_NOT_AUTHORIZED ||
                 rpcError.Code == CLightningErrorCode.RUNE_NOT_PERMITTED ||
                 rpcError.Code == CLightningErrorCode.RUNE_BLACKLISTED))
            {
                cancellation.ThrowIfCancellationRequested();
                var refreshed = await GetRuneAsync(cancellation, rune);
                if (refreshed != rune)
                {
                    rune = refreshed;
                    continue;
                }
            }
            throw error;
        }
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

    // Bounds encoded credentials before decoding.
    internal const int MaxRuneSize = 64 * 1024;

    private async Task<string> GetRuneAsync(CancellationToken cancellation, string rejected = null)
    {
        await _runeLock.WaitAsync(cancellation);
        try
        {
            // Compare string references so concurrent rejections share a reload,
            // even when the file produces a new string with the same value.
            if (_cachedRune == null || ReferenceEquals(_cachedRune, rejected))
            {
                var rune = Settings.Rune;
                if (rune == null)
                {
#if NETSTANDARD2_0
                    rune = File.ReadAllText(Settings.RuneFilePath).Trim();
#else
                    rune = (await File.ReadAllTextAsync(Settings.RuneFilePath, cancellation)).Trim();
#endif
                }
                cancellation.ThrowIfCancellationRequested();
                if (!IsRuneFormatValid(rune))
                    throw new InvalidOperationException("The rune must be URL-safe Base64 containing at least 32 bytes and no NUL in its restrictions");
                _cachedRune = rune;
            }
            return _cachedRune;
        }
        finally
        {
            _runeLock.Release();
        }
    }

    internal static bool IsRuneFormatValid(string rune)
    {
        if (rune.Length == 0 || rune.Length > MaxRuneSize)
            return false;
        try
        {
            // CLN accepts both padded and unpadded Base64url.
            var base64 = rune.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var decoded = Convert.FromBase64String(base64);
            // Convert also accepts whitespace and the ordinary Base64 alphabet.
            var encoded = Convert.ToBase64String(decoded).Replace('+', '-').Replace('/', '_');
            // Match CLN's basic envelope checks without duplicating its restriction
            // parser. Only the node can verify authenticity and authorization.
            return encoded.TrimEnd('=') == rune.TrimEnd('=') &&
                   decoded.Length >= 32 && Array.IndexOf(decoded, (byte)0, 32) == -1;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
