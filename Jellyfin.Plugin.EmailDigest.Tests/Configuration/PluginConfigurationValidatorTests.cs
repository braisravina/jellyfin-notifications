using System;
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
}
