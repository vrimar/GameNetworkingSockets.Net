using Valve.Sockets;
using Xunit;

namespace GameNetworkingSockets.Net.Tests;

public class NetworkingCertificateTests
{
    private static readonly DateTimeOffset s_now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly NetworkingCertificateAuthority s_authority =
        NetworkingCertificateAuthority.Create(s_now, TimeSpan.FromDays(10));

    [Fact]
    public void IssuedCertificateNamesItsIdentityExpiryAndKey()
    {
        var certificate = s_authority.Issue("str:game-1", s_now, TimeSpan.FromDays(3));

        Assert.Equal(
            new NetworkingCertificateInfo("str:game-1", s_now.AddDays(3), HasPrivateKey: true),
            NetworkingCertificate.Read(certificate));
    }

    [Fact]
    public void RootCertificateNamesNoIdentityAndCarriesNoPrivateKey()
    {
        Assert.Equal(
            new NetworkingCertificateInfo(null, s_now.AddDays(10), HasPrivateKey: false),
            NetworkingCertificate.Read(Convert.FromBase64String(s_authority.RootCertificate)));
        Assert.Equal(s_now.AddDays(10), s_authority.Expiry);
    }

    [Fact]
    public void ExportedAuthorityImportsUnderTheSameRoot()
    {
        var exported = s_authority.Export();
        using var imported = NetworkingCertificateAuthority.Import(exported);

        Assert.True(NetworkingCertificate.Read(exported).HasPrivateKey);
        Assert.Equal(s_authority.RootCertificate, imported.RootCertificate);
        Assert.Equal(s_authority.Expiry, imported.Expiry);
        Assert.Equal("str:game-1", NetworkingCertificate.Read(imported.Issue("str:game-1", s_now, TimeSpan.FromDays(1))).Identity);
    }

    [Fact]
    public void IssuedCertificateIsNotAnAuthority()
    {
        var certificate = s_authority.Issue("str:game-1", s_now, TimeSpan.FromDays(1));

        Assert.Throws<ArgumentException>(() => NetworkingCertificateAuthority.Import(certificate));
    }

    [Fact]
    public void PublicRootCannotSign()
    {
        Assert.Throws<ArgumentException>(() =>
            NetworkingCertificateAuthority.Import(Convert.FromBase64String(s_authority.RootCertificate)));
    }

    [Fact]
    public void AuthorityWithABrokenSelfSignatureIsRefused()
    {
        var exported = s_authority.Export();
        exported[^1] ^= 1;

        Assert.Throws<ArgumentException>(() => NetworkingCertificateAuthority.Import(exported));
    }

    [Fact]
    public void CertificateCannotOutliveItsAuthority()
    {
        Assert.Throws<ArgumentException>(() => s_authority.Issue("str:game-1", s_now, TimeSpan.FromDays(11)));
    }

    [Fact]
    public void CertificateCanLapseWithItsAuthorityWhateverTheSubSecondStart()
    {
        var now = s_now.AddMilliseconds(750);
        using var authority = NetworkingCertificateAuthority.Create(now, TimeSpan.FromDays(10));

        var certificate = authority.Issue("str:game-1", now, TimeSpan.FromDays(10));

        Assert.Equal(authority.Expiry, NetworkingCertificate.Read(certificate).Expiry);
    }

    [Fact]
    public void LifetimeMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkingCertificateAuthority.Create(s_now, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => s_authority.Issue("str:game-1", s_now, -TimeSpan.FromDays(1)));
    }

    [Fact]
    public void ExpiryCannotPassTheSigned32BitUnixTime()
    {
        var last = DateTimeOffset.FromUnixTimeSeconds(int.MaxValue);
        using var authority = NetworkingCertificateAuthority.Create(s_now, last - s_now);

        Assert.Equal(last, authority.Expiry);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NetworkingCertificateAuthority.Create(s_now, last - s_now + TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkingCertificateAuthority.Create(s_now, TimeSpan.MaxValue));
    }

    [Theory]
    [InlineData("")]
    [InlineData("game-1")]
    [InlineData(":game-1")]
    [InlineData("str:")]
    [InlineData("str:abcdefghijklmnopqrstuvwxyz012345")]
    [InlineData("str:a\0b")]
    [InlineData("STR:game-1")]
    [InlineData("gen:")]
    [InlineData("gen:abc")]
    [InlineData("gen:zz")]
    [InlineData("steamid:76561197960287930")]
    [InlineData("ip:127.0.0.1")]
    [InlineData("psn:1")]
    public void IdentityACertificateCannotCarryIsRefused(string identity)
    {
        Assert.Throws<ArgumentException>(() => s_authority.Issue(identity, s_now, TimeSpan.FromDays(1)));
    }

    [Fact]
    public void IdentityThatIsNotUnicodeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => s_authority.Issue("str:" + '\uD800', s_now, TimeSpan.FromDays(1)));
    }

    [Theory]
    [InlineData("str:a")]
    [InlineData("str:abcdefghijklmnopqrstuvwxyz01234")]
    [InlineData("gen:0a")]
    [InlineData("gen:DEADbeef")]
    [InlineData("gen:00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff")]
    public void IdentityACertificateCanCarryIsIssued(string identity)
    {
        Assert.Equal(identity, NetworkingCertificate.Read(s_authority.Issue(identity, s_now, TimeSpan.FromDays(1))).Identity);
    }

    [Fact]
    public void DisposedAuthorityCannotSign()
    {
        var authority = NetworkingCertificateAuthority.Create(s_now, TimeSpan.FromDays(1));
        authority.Dispose();

        Assert.Throws<ObjectDisposedException>(() => authority.Issue("str:game-1", s_now, TimeSpan.FromHours(1)));
        Assert.Throws<ObjectDisposedException>(authority.Export);
    }

    [Fact]
    public void BytesThatAreNotACertificateAreRefused()
    {
        Assert.Throws<FormatException>(() => NetworkingCertificate.Read([0x22, 0x05, 0x01]));
        Assert.Throws<FormatException>(() => NetworkingCertificate.Read([]));
        Assert.Throws<FormatException>(() => NetworkingCertificateAuthority.Import([0x22, 0x05, 0x01]));
    }
}
