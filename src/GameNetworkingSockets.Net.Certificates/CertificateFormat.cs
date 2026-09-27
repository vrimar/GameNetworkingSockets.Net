using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace Valve.Sockets;

internal static class CertificateFormat
{
    private const int SignedPrivateKey = 1;
    private const int SignedCert = 4;
    private const int SignedCaKeyId = 5;
    private const int SignedCaSignature = 6;

    private const int CertKeyType = 1;
    private const int CertKeyData = 2;
    private const int CertTimeCreated = 8;
    private const int CertTimeExpiry = 9;
    private const int CertAppIds = 10;
    private const int CertIdentity = 12;

    private const int KeyTypeEd25519 = 1;
    private const int MaxBlobBytes = 512;

    private const string GenericStringPrefix = "str:";
    private const string GenericBytesPrefix = "gen:";
    private const int MaxGenericStringBytes = 31;
    private const int MaxGenericBytes = 32;

    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly SearchValues<char> s_hexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    public static readonly SignatureAlgorithm Ed25519 = SignatureAlgorithm.Ed25519;

    public readonly ref struct Signed(ReadOnlySpan<byte> cert, ulong caKeyId, ReadOnlySpan<byte> caSignature, ReadOnlySpan<byte> privateKey)
    {
        public ReadOnlySpan<byte> Cert { get; } = cert;
        public ulong CaKeyId { get; } = caKeyId;
        public ReadOnlySpan<byte> CaSignature { get; } = caSignature;
        public ReadOnlySpan<byte> PrivateKey { get; } = privateKey;
    }

    public readonly ref struct Body(ReadOnlySpan<byte> publicKey, string? identity, uint expiry)
    {
        public ReadOnlySpan<byte> PublicKey { get; } = publicKey;
        public string? Identity { get; } = identity;
        public uint Expiry { get; } = expiry;
    }

    public static Signed ReadSigned(ReadOnlySpan<byte> blob)
    {
        ReadOnlySpan<byte> cert = default, caSignature = default, privateKey = default;
        ulong caKeyId = 0;

        var reader = new ProtoReader(blob);
        while (reader.Next(out var field, out var wireType))
        {
            switch (field, wireType)
            {
                case (SignedPrivateKey, ProtoReader.LengthDelimited):
                    privateKey = reader.Bytes();
                    break;
                case (SignedCert, ProtoReader.LengthDelimited):
                    cert = reader.Bytes();
                    break;
                case (SignedCaKeyId, ProtoReader.Fixed64Wire):
                    caKeyId = reader.Fixed64();
                    break;
                case (SignedCaSignature, ProtoReader.LengthDelimited):
                    caSignature = reader.Bytes();
                    break;
                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        if (cert.IsEmpty)
            throw new FormatException("Not a certificate: it holds no signed cert.");

        return new(cert, caKeyId, caSignature, privateKey);
    }

    public static Body ReadBody(ReadOnlySpan<byte> blob)
    {
        ReadOnlySpan<byte> publicKey = default;
        string? identity = null;
        uint? expiry = null;

        var reader = new ProtoReader(blob);
        while (reader.Next(out var field, out var wireType))
        {
            switch (field, wireType)
            {
                case (CertKeyData, ProtoReader.LengthDelimited):
                    publicKey = reader.Bytes();
                    break;
                case (CertIdentity, ProtoReader.LengthDelimited):
                    identity = Encoding.UTF8.GetString(reader.Bytes());
                    break;
                case (CertTimeExpiry, ProtoReader.Fixed32Wire):
                    expiry = reader.Fixed32();
                    break;
                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        if (publicKey.IsEmpty || expiry is null)
            throw new FormatException("Not a certificate: it names no key or no expiry.");

        return new(publicKey, identity, expiry.Value);
    }

    public static byte[] WriteBody(ReadOnlySpan<byte> publicKey, string? identity, uint appId, uint created, uint expiry)
    {
        Span<byte> buffer = stackalloc byte[MaxBlobBytes];
        var writer = new ProtoWriter(buffer);
        writer.Varint(CertKeyType, KeyTypeEd25519);
        writer.Bytes(CertKeyData, publicKey);
        writer.Fixed32(CertTimeCreated, created);
        writer.Fixed32(CertTimeExpiry, expiry);

        if (identity is not null)
        {
            writer.Varint(CertAppIds, appId);
            writer.Bytes(CertIdentity, s_utf8.GetBytes(identity));
        }

        return writer.Written.ToArray();
    }

    public static byte[] WriteSigned(ReadOnlySpan<byte> cert, ulong caKeyId, ReadOnlySpan<byte> caSignature, Key? privateKey)
    {
        Span<byte> buffer = stackalloc byte[MaxBlobBytes];
        Span<byte> keyBytes = stackalloc byte[Ed25519.PrivateKeySize];
        try
        {
            var writer = new ProtoWriter(buffer);
            if (privateKey is not null)
            {
                if (!privateKey.TryExport(KeyBlobFormat.RawPrivateKey, keyBytes, out var written))
                    throw new UnreachableException();
                writer.Bytes(SignedPrivateKey, keyBytes[..written]);
            }

            writer.Bytes(SignedCert, cert);
            writer.Fixed64(SignedCaKeyId, caKeyId);
            writer.Bytes(SignedCaSignature, caSignature);
            return writer.Written.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    public static ulong KeyId(ReadOnlySpan<byte> publicKey) =>
        BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(publicKey));

    public static (uint Created, uint Expiry) Window(DateTimeOffset now, TimeSpan lifetime)
    {
        const string range =
            "A certificate's times must fall between 1970 and 2038-01-19 03:14:07 UTC: GameNetworkingSockets on Windows reads a later expiry as already passed.";

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        var created = now.ToUnixTimeSeconds();
        if (created is < 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(now), now, range);

        var expiry = lifetime < DateTimeOffset.MaxValue - now ? (now + lifetime).ToUnixTimeSeconds() : long.MaxValue;
        if (expiry > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, range);

        return ((uint)created, (uint)expiry);
    }

    public static void ValidateIdentity(string identity)
    {
        if (identity.StartsWith(GenericStringPrefix, StringComparison.Ordinal))
        {
            var value = identity.AsSpan(GenericStringPrefix.Length);
            if (!value.IsEmpty && !value.Contains('\0') && Utf8ByteCount(value) <= MaxGenericStringBytes)
                return;
        }
        else if (identity.StartsWith(GenericBytesPrefix, StringComparison.Ordinal))
        {
            var hex = identity.AsSpan(GenericBytesPrefix.Length);
            if (hex.Length is >= 2 and <= 2 * MaxGenericBytes && hex.Length % 2 == 0 && !hex.ContainsAnyExcept(s_hexDigits))
                return;
        }

        throw new ArgumentException(
            $"'{identity}' is not an identity a certificate can carry: expected 'str:' and 1 to {MaxGenericStringBytes} bytes, such as 'str:game-1', or 'gen:' and 2 to {2 * MaxGenericBytes} hex digits.",
            nameof(identity));
    }

    private static int Utf8ByteCount(ReadOnlySpan<char> value)
    {
        try
        {
            return s_utf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            return int.MaxValue;
        }
    }
}
