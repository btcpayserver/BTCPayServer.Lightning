using System;

namespace BTCPayServer.Lightning.CLightning;

/// <summary>
/// Settings for a clnrest connection. The client sends each call with the rune in a header.
/// An HttpClient given to <see cref="CLightningClient"/> must not follow redirects, or it sends the rune to the redirect target.
/// </summary>
public class CLightningRestSettings
{
    public CLightningRestSettings()
    {
    }

    public CLightningRestSettings(Uri server)
    {
        Server = server;
    }

    /// <summary>
    /// The root URL of clnrest, without a path, query or fragment.
    /// </summary>
    public Uri Server { get; set; }
    /// <summary>
    /// The rune has to allow each method the client calls. Listen needs listinvoices and waitanyinvoice.
    /// Basic Base64url format is checked when first used, then the validated rune is cached per client.
    /// The encoded rune is limited to 64 KB.
    /// </summary>
    public string Rune { get; set; }
    /// <summary>
    /// A file holding the rune. It is read lazily and the validated rune is cached per client.
    /// On an explicit CLN rune rejection, the file is reloaded and the request is retried once only if the rune changed.
    /// Revoke the old rune to trigger rotation; transport errors never trigger a retry.
    /// Any filename is accepted. After reading, basic rune format and the 64 KB encoded-size limit are checked before sending.
    /// The client reads a local file and sends its content to <see cref="Server"/>, so don't let untrusted users set it.
    /// </summary>
    public string RuneFilePath { get; set; }
}
