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
    /// </summary>
    public string Rune { get; set; }
    /// <summary>
    /// A file holding the rune. It is read on each request, so the rune can be rotated.
    /// The client reads a local file and sends its content to <see cref="Server"/>, so don't let untrusted users set it.
    /// </summary>
    public string RuneFilePath { get; set; }
    /// <summary>
    /// The SHA256 of the server certificate (clnrest's server.pem). It needs an https server.
    /// </summary>
    public byte[] CertificateThumbprint { get; set; }
    /// <summary>
    /// A PEM file with the server certificate, read once when the client is created (at most 64 KB),
    /// so a renewed certificate needs a new client. It needs an https server.
    /// The client reads a local file, so don't let untrusted users set it.
    /// </summary>
    public string CertificateFilePath { get; set; }
    public bool AllowInsecure { get; set; }
}
