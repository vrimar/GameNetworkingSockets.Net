using System.Security.Cryptography;

namespace Valve.Sockets;

internal static class TrustedRoot
{
    private const int SignedPrivateKey = 1;

    public static bool CarriesPrivateKey(string? base64Cert)
    {
        if (base64Cert is null)
            return false;

        var blob = new byte[base64Cert.Length * 3 / 4 + 1];
        try
        {
            if (!TryDecodeAsNative(base64Cert, blob, out var length))
                return false;

            var reader = new ProtoReader(blob.AsSpan(0, length));
            while (reader.Next(out var field, out var wireType))
            {
                if (field == SignedPrivateKey && wireType == ProtoReader.LengthDelimited)
                    return true;
                reader.Skip(wireType);
            }

            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
        }
    }

    // Exactly as lenient as the native decoder, so no blob it accepts slips past the check.
    private static bool TryDecodeAsNative(string base64, Span<byte> destination, out int length)
    {
        length = 0;
        int bits = 0, bitCount = 0;

        foreach (var c in base64)
        {
            if (c is '=' or '\0')
                break;
            if (c is ' ' or '\t' or '\r' or '\n')
                continue;

            var value = c switch
            {
                >= 'A' and <= 'Z' => c - 'A',
                >= 'a' and <= 'z' => c - 'a' + 26,
                >= '0' and <= '9' => c - '0' + 52,
                '+' => 62,
                '/' => 63,
                _ => -1,
            };
            if (value < 0)
                return false;

            bits = (bits << 6) | value;
            bitCount += 6;
            if (bitCount >= 8)
            {
                bitCount -= 8;
                destination[length++] = (byte)(bits >> bitCount);
                bits &= (1 << bitCount) - 1;
            }
        }

        return true;
    }
}
