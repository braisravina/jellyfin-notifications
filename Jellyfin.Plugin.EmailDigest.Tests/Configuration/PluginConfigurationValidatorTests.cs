using System;
using System.Linq;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Configuration;

public class PluginConfigurationValidatorTests
{
    private static PluginConfiguration ValidConfiguration()
    {
        return new PluginConfiguration
        {
            SmtpEnabled = true,
            SmtpHost = "smtp.example.com",
            SmtpPort = 587,
            SmtpSecurity = SmtpSecurityMode.StartTls,
            SenderEmailAddress = "digest@example.com",
            DefaultLanguage = "en",
            FallbackLookbackHours = 168
        };
    }

    [Fact]
    public void Validate_DefaultConfiguration_HasNoErrors()
    {
        var errors = PluginConfigurationValidator.Validate(new PluginConfiguration());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ValidConfiguration_HasNoErrors()
    {
        var errors = PluginConfigurationValidator.Validate(ValidConfiguration());

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_PortOutOfRange_ReportsError(int port)
    {
        var config = ValidConfiguration();
        config.SmtpPort = port;

        var errors = PluginConfigurationValidator.Validate(config);

        Assert.Contains(errors, e => e.Contains("SmtpPort", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_UndefinedSmtpSecurity_ReportsError()
    {
        var config = ValidConfiguration();
        config.SmtpSecurity = (SmtpSecurityMode)999;

        var errors = PluginConfigurationValidator.Validate(config);

        Assert.Contains(errors, e => e.Contains("SmtpSecurity", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SmtpEnabledWithoutHost_ReportsError()
    {
        var config = ValidConfiguration();
        config.SmtpHost = string.Empty;

        var errors = PluginConfigurationValidator.Validate(config);

        Assert.Contains(errors, e => e.Contains("SmtpHost", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData("missing-at-sign.com")]
    public void Validate_SmtpEnabledWithInvalidSenderAddress_ReportsError(string senderAddress)
    {
        var config = ValidConfiguration();
        config.SenderEmailAddress = senderAddress;

        var errors = PluginConfigurationValidator.Validate(config);

        Assert.Contains(errors, e => e.Contains("SenderEmailAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SmtpDisabledWithEmptySenderAddress_HasNoError()
    {
        var config = ValidConfiguration();
        config.SmtpEnabled = false;
        config.SenderEmailAddress = string.Empty;

        var errors = PluginConfigurationValidator.Validate(config);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("xx")]
    [InlineData("")]
    public void Validate_UnsupportedDefaultLanguage_ReportsError(string language)
    {
        var config = ValidConfiguration();
        config.DefaultLanguage = language;

        var errors = PluginConfigurationValidator.Validate(config);

        Assert.Contains(errors, e => e.Contains("DefaultLanguage", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NegativeFallbackLookback_ReportsError()
    {
        var config = ValidConfiguration();
        config.FallbackLookbackHours = -1;

        var errors = PluginConfigurationValidator.Validate(config);

        Assert.Contains(errors, e => e.Contains("FallbackLookbackHours", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MonitoredLibraryWithEmptyId_ReportsError()
    {
        var config = ValidConfiguration();
        config.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = Guid.Empty, Category = MediaCategory.Movies });

        var errors = PluginConfigurationValidator.Validate(config);

        Assert.Contains(errors, e => e.Contains("LibraryId", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MonitoredLibraryWithUndefinedCategory_ReportsError()
    {
        var config = ValidConfiguration();
        config.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = Guid.NewGuid(), Category = (MediaCategory)999 });

        var errors = PluginConfigurationValidator.Validate(config);

        Assert.Contains(errors, e => e.Contains("Category", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ValidMonitoredLibrary_HasNoError()
    {
        var config = ValidConfiguration();
        config.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = Guid.NewGuid(), Category = MediaCategory.Anime });

        var errors = PluginConfigurationValidator.Validate(config);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(121)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_TimeoutOutOfRange_ReportsError(int seconds)
    {
        var config = ValidConfiguration();
        config.SmtpTimeoutSeconds = seconds;

        Assert.Contains(PluginConfigurationValidator.Validate(config), e => e.Contains("SmtpTimeoutSeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(120)]
    public void Validate_TimeoutInRange_HasNoError(int seconds)
    {
        var config = ValidConfiguration();
        config.SmtpTimeoutSeconds = seconds;

        Assert.Empty(PluginConfigurationValidator.Validate(config));
    }

    [Theory]
    [InlineData("smtp.example.com", true)]
    [InlineData("mail", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("::1", true)]
    [InlineData("smtp.example.com\r\nRCPT TO:<x@y.z>", false)]
    [InlineData("smtp example.com", false)]
    [InlineData("-bad.example.com", false)]
    [InlineData("exa_mple.com", false)]
    [InlineData("smtp.example.com:25", false)]
    [InlineData("http://smtp.example.com", false)]
    public void Validate_Host(string host, bool valid)
    {
        var config = ValidConfiguration();
        config.SmtpHost = host;

        var errors = PluginConfigurationValidator.Validate(config);

        Assert.Equal(valid, !errors.Any(e => e.Contains("SmtpHost", StringComparison.Ordinal)));
    }

    [Fact]
    public void Validate_UnencryptedWithoutCredentials_IsAllowedBecauseTheAdministratorChoseIt()
    {
        var config = ValidConfiguration();
        config.SmtpSecurity = SmtpSecurityMode.None;
        config.SmtpPort = 25;

        Assert.Empty(PluginConfigurationValidator.Validate(config));
    }

    [Fact]
    public void Validate_UnencryptedWithUsername_IsRejectedSoCredentialsAreNeverSentInClear()
    {
        var config = ValidConfiguration();
        config.SmtpSecurity = SmtpSecurityMode.None;
        config.SmtpUsername = "mailer";

        Assert.Contains(PluginConfigurationValidator.Validate(config), e => e.Contains("unencrypted", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(SmtpSecurityMode.StartTls)]
    [InlineData(SmtpSecurityMode.ImplicitTls)]
    public void Validate_EncryptedWithUsername_IsAllowed(SmtpSecurityMode mode)
    {
        var config = ValidConfiguration();
        config.SmtpSecurity = mode;
        config.SmtpUsername = "mailer";

        Assert.Empty(PluginConfigurationValidator.Validate(config));
    }

    [Theory]
    [InlineData("Jellyfin\r\nBcc: x@evil.example")]
    [InlineData("name\nwith newline")]
    public void Validate_SenderDisplayNameWithLineBreak_IsRejected(string name)
    {
        var config = ValidConfiguration();
        config.SenderDisplayName = name;

        Assert.Contains(PluginConfigurationValidator.Validate(config), e => e.Contains("SenderDisplayName", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_UsernameWithLineBreak_IsRejected()
    {
        var config = ValidConfiguration();
        config.SmtpUsername = "user\nname";

        Assert.Contains(PluginConfigurationValidator.Validate(config), e => e.Contains("SmtpUsername", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SenderAddressWithLineBreak_IsRejected()
    {
        var config = ValidConfiguration();
        config.SenderEmailAddress = "a@b.co\nBcc: x@y.zz";

        Assert.NotEmpty(PluginConfigurationValidator.Validate(config));
    }
}
