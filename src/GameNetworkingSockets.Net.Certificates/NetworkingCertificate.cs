namespace Valve.Sockets;

/// <summary>
/// What a certificate names: its identity (<see langword="null"/> for a root authority), when peers
/// stop honouring it, and whether it carries its private key.
/// </summary>
public readonly record struct NetworkingCertificateInfo(string? Identity, DateTimeOffset Expiry, bool HasPrivateKey);

/// <summary>
/// Reads GameNetworkingSockets certificates: serialized <c>CMsgSteamDatagramCertificateSigned</c> blobs, such
/// as the ones <see cref="NetworkingCertificateAuthority.Issue"/> returns.
/// </summary>
public static class NetworkingCertificate
{
    /// <summary>What <paramref name="certificate"/> names. Reading it does not check its signature.</summary>
    /// <exception cref="FormatException"><paramref name="certificate"/> is not a certificate.</exception>
    public static NetworkingCertificateInfo Read(ReadOnlySpan<byte> certificate)
    {
        var signed = CertificateFormat.ReadSigned(certificate);
        var body = CertificateFormat.ReadBody(signed.Cert);
        return new(body.Identity, DateTimeOffset.FromUnixTimeSeconds(body.Expiry), !signed.PrivateKey.IsEmpty);
    }
}
