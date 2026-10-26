using Jellyfin.Plugin.EmailDigest.Configuration;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Configuration;

public sealed class PublicBaseUrlTests
{
    [Theory]
    [InlineData("https://jellyfin.example.org", "https://jellyfin.example.org")]
    [InlineData("https://jellyfin.example.org/", "https://jellyfin.example.org")]
    [InlineData("  https://example.org/jf/  ", "https://example.org/jf")]
    [InlineData("http://192.168.1.10:8096", "http://192.168.1.10:8096")]
    public void AcceptableAddresses_AreNormalized(string value, string expected)
    {
        Assert.Equal(expected, PublicBaseUrl.TryNormalize(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("jellyfin.example.org")]
    [InlineData("ftp://example.org")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@example.org")]
    [InlineData("https://example.org?x=1")]
    [InlineData("https://example.org/#frag")]
    [InlineData("https://exa mple.org")]
    [InlineData("https://example.org\\evil.test")]
    [InlineData("https://example.org\r\nHost: evil.test")]
    public void UnacceptableAddresses_AreRefused(string? value)
    {
        Assert.Null(PublicBaseUrl.TryNormalize(value));
    }

    [Fact]
    public void Link_IsBuiltFromTheConfiguredBase_AndTheTokenIsEscaped()
    {
        Assert.Equal(
            "https://example.org/jf/EmailDigest/Verify?token=abc_-123",
            PublicBaseUrl.Link("https://example.org/jf/", PublicBaseUrl.VerifyPath, "abc_-123"));
        Assert.Equal(
            "https://example.org/EmailDigest/Verify?token=a%26b%3Dc",
            PublicBaseUrl.Link("https://example.org", PublicBaseUrl.VerifyPath, "a&b=c"));
    }

    [Fact]
    public void Link_WithoutAUsableBase_IsNull()
    {
        Assert.Null(PublicBaseUrl.Link(string.Empty, PublicBaseUrl.VerifyPath, "t"));
        Assert.Null(PublicBaseUrl.Link("nonsense", PublicBaseUrl.UnsubscribePath, "t"));
    }

    [Fact]
    public void Validator_AcceptsEmptyAndGood_RejectsBad_AndChecksTheLifetime()
    {
        var config = new PluginConfiguration();
        Assert.Empty(PluginConfigurationValidator.Validate(config));

        config.PublicBaseUrl = "https://example.org";
        Assert.Empty(PluginConfigurationValidator.Validate(config));

        config.PublicBaseUrl = "https://u:p@example.org";
        Assert.Contains(PluginConfigurationValidator.Validate(config), e => e.StartsWith("PublicBaseUrl", System.StringComparison.Ordinal));

        config.PublicBaseUrl = string.Empty;
        config.VerificationLinkMinutes = 4;
        Assert.Contains(PluginConfigurationValidator.Validate(config), e => e.StartsWith("VerificationLinkMinutes", System.StringComparison.Ordinal));
        config.VerificationLinkMinutes = 1441;
        Assert.Contains(PluginConfigurationValidator.Validate(config), e => e.StartsWith("VerificationLinkMinutes", System.StringComparison.Ordinal));
        config.VerificationLinkMinutes = 60;
        Assert.Empty(PluginConfigurationValidator.Validate(config));
    }

    [Fact]
    public void Defaults_AreSafe_NoPublicAddressAndAnHourLifetime()
    {
        var config = new PluginConfiguration();

        Assert.Equal(string.Empty, config.PublicBaseUrl);
        Assert.Equal(60, config.VerificationLinkMinutes);
    }
}
