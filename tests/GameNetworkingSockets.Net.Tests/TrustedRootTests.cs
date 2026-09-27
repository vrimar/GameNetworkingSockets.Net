using System.Text.RegularExpressions;
using Valve.Sockets;
using Xunit;

namespace GameNetworkingSockets.Net.Tests;

public partial class TrustedRootTests
{
    private static readonly NetworkingCertificateAuthority s_authority =
        NetworkingCertificateAuthority.Create(DateTimeOffset.UtcNow, TimeSpan.FromDays(1));

    private static readonly string s_exported = Convert.ToBase64String(s_authority.Export());

    [Fact]
    public void AuthorityIsRecognisedByItsPrivateKey()
    {
        Assert.True(TrustedRoot.CarriesPrivateKey(s_exported));
        Assert.False(TrustedRoot.CarriesPrivateKey(s_authority.RootCertificate));
    }

    [Fact]
    public void AuthorityIsRecognisedInEveryFormTheNativeDecoderAccepts()
    {
        Assert.True(TrustedRoot.CarriesPrivateKey(s_exported.TrimEnd('=')));
        Assert.True(TrustedRoot.CarriesPrivateKey(EveryFortyChars().Replace(s_exported, "$0\r\n\t ")));
        Assert.True(TrustedRoot.CarriesPrivateKey(s_exported + "\0trailing garbage"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void AnythingElseIsLeftToTheNativeSide(string base64Cert)
    {
        Assert.False(TrustedRoot.CarriesPrivateKey(base64Cert));
    }

    [GeneratedRegex(".{40}")]
    private static partial Regex EveryFortyChars();
}
