using NSec.Cryptography;

namespace Valve.Sockets;

/// <summary>
/// A root authority: a self-signed GameNetworkingSockets certificate, bound to no identity, and the private key
/// that signs the certificates it issues. Peers trust its <see cref="RootCertificate"/>; the authority itself,
/// saved with <see cref="Export"/>, stays offline.
/// </summary>
/// <remarks>
/// A peer verifies a certificate only against a root it was given through <c>AddTrustedRootCA</c>, and a
/// process must trust the root of its own certificate too, or <c>SetCertificate</c> cannot read it. The private
/// key lives in NSec's protected memory until the authority is disposed.
/// </remarks>
public sealed class NetworkingCertificateAuthority : IDisposable
{
    private static KeyCreationParameters Exportable =>
        new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };

    private readonly Key _key;
    private readonly byte[] _cert;
    private readonly byte[] _signature;
    private readonly ulong _keyId;
    private readonly uint _expiry;

    private NetworkingCertificateAuthority(Key key, ReadOnlySpan<byte> publicKey, byte[] cert, byte[] signature, uint expiry)
    {
        _key = key;
        _cert = cert;
        _signature = signature;
        _keyId = CertificateFormat.KeyId(publicKey);
        _expiry = expiry;
        RootCertificate = Convert.ToBase64String(CertificateFormat.WriteSigned(cert, _keyId, signature, privateKey: null));
    }

    /// <summary>
    /// The public root, base64-encoded as <c>AddTrustedRootCA</c> takes it. It carries no private key, so it is
    /// safe to ship in every client.
    /// </summary>
    public string RootCertificate { get; }

    /// <summary>When peers stop honouring the authority, and with it every certificate it issued.</summary>
    public DateTimeOffset Expiry => DateTimeOffset.FromUnixTimeSeconds(_expiry);

    /// <summary>A new root authority with a fresh private key. Save it with <see cref="Export"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is not positive, or the
    /// validity window falls outside 1970 to 2038-01-19.</exception>
    public static NetworkingCertificateAuthority Create(DateTimeOffset now, TimeSpan lifetime)
    {
        var (created, expiry) = CertificateFormat.Window(now, lifetime);

        var key = Key.Create(CertificateFormat.Ed25519, Exportable);
        try
        {
            var publicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
            var cert = CertificateFormat.WriteBody(publicKey, identity: null, appId: 0, created, expiry);
            return new(key, publicKey, cert, CertificateFormat.Ed25519.Sign(key, cert), expiry);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>Loads an authority saved with <see cref="Export"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="authority"/> is a certificate but not a root
    /// authority: it names an identity, carries no private key, is not self-signed, or holds a private key
    /// that does not match it.</exception>
    /// <exception cref="FormatException"><paramref name="authority"/> is not a certificate.</exception>
    public static NetworkingCertificateAuthority Import(ReadOnlySpan<byte> authority)
    {
        var signed = CertificateFormat.ReadSigned(authority);
        var body = CertificateFormat.ReadBody(signed.Cert);

        if (body.Identity is not null)
            throw new ArgumentException(
                $"The certificate names {body.Identity}, so it is not a root authority: GameNetworkingSockets keeps identity-bound certificates out of its trust store.",
                nameof(authority));
        if (signed.PrivateKey.IsEmpty)
            throw new ArgumentException("The certificate carries no private key, so it cannot sign.", nameof(authority));

        var publicKey = PublicKey.Import(CertificateFormat.Ed25519, body.PublicKey, KeyBlobFormat.RawPublicKey);
        if (signed.CaKeyId != CertificateFormat.KeyId(body.PublicKey)
            || !CertificateFormat.Ed25519.Verify(publicKey, signed.Cert, signed.CaSignature))
            throw new ArgumentException("The certificate is not self-signed, so it is not a root authority.", nameof(authority));

        var key = Key.Import(CertificateFormat.Ed25519, signed.PrivateKey, KeyBlobFormat.RawPrivateKey, Exportable);
        if (!key.PublicKey.Equals(publicKey))
        {
            key.Dispose();
            throw new ArgumentException("The authority's private key does not match its certificate.", nameof(authority));
        }

        return new(key, body.PublicKey, signed.Cert.ToArray(), signed.CaSignature.ToArray(), body.Expiry);
    }

    /// <summary>
    /// The authority with its private key, for <see cref="Import"/>. Anyone holding it can mint certificates
    /// every peer trusts: keep it offline, and never pass it to <c>AddTrustedRootCA</c>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The authority has been disposed.</exception>
    public byte[] Export() => CertificateFormat.WriteSigned(_cert, _keyId, _signature, _key);

    /// <summary>
    /// A certificate for <paramref name="identity"/>, signed by this authority and carrying a fresh private key:
    /// the blob a process passes to <c>SetCertificate</c> to take that identity.
    /// </summary>
    /// <param name="identity"><c>str:</c> and 1 to 31 bytes, such as <c>str:game-1</c>, or <c>gen:</c> and 2 to
    /// 64 hex digits.</param>
    /// <param name="now">When the certificate is created.</param>
    /// <param name="lifetime">How long peers honour it. It cannot outlive the authority.</param>
    /// <param name="appId">The app id peers run as. A peer refuses an identity-bound certificate that does not
    /// list its own, and the open-source build runs as 0.</param>
    /// <exception cref="ArgumentException"><paramref name="identity"/> is not one a certificate can carry, or
    /// the certificate would outlive the authority.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is not positive, or the
    /// validity window falls outside 1970 to 2038-01-19.</exception>
    /// <exception cref="ObjectDisposedException">The authority has been disposed.</exception>
    public byte[] Issue(string identity, DateTimeOffset now, TimeSpan lifetime, uint appId = 0)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var (created, expiry) = CertificateFormat.Window(now, lifetime);
        CertificateFormat.ValidateIdentity(identity);

        if (expiry > _expiry)
            throw new ArgumentException($"The certificate would outlive its authority, which expires {Expiry:u}.", nameof(lifetime));

        using var key = Key.Create(CertificateFormat.Ed25519, Exportable);
        var cert = CertificateFormat.WriteBody(key.PublicKey.Export(KeyBlobFormat.RawPublicKey), identity, appId, created, expiry);
        return CertificateFormat.WriteSigned(cert, _keyId, CertificateFormat.Ed25519.Sign(_key, cert), key);
    }

    /// <summary>Releases the private key.</summary>
    public void Dispose() => _key.Dispose();
}
