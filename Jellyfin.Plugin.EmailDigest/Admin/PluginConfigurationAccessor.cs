using System;
using Jellyfin.Plugin.EmailDigest.Configuration;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <inheritdoc />
public sealed class PluginConfigurationAccessor : ISmtpConfigurationAccessor
{
    /// <inheritdoc />
    public PluginConfiguration GetCopy()
    {
        var current = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Email Digest plugin instance is not initialized yet.");

        var copy = new PluginConfiguration
        {
            SmtpEnabled = current.SmtpEnabled,
            SmtpHost = current.SmtpHost,
            SmtpPort = current.SmtpPort,
            SmtpSecurity = current.SmtpSecurity,
            SmtpUsername = current.SmtpUsername,
            SmtpTimeoutSeconds = current.SmtpTimeoutSeconds,
            SenderEmailAddress = current.SenderEmailAddress,
            SenderDisplayName = current.SenderDisplayName,
            DigestEnabled = current.DigestEnabled,
            DefaultLanguage = current.DefaultLanguage,
            FallbackLookbackHours = current.FallbackLookbackHours
        };

        foreach (var library in current.MonitoredLibraries)
        {
            copy.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = library.LibraryId, Category = library.Category });
        }

        return copy;
    }

    /// <inheritdoc />
    public void Save(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Email Digest plugin instance is not initialized yet.");
        plugin.UpdateConfiguration(configuration);
    }
}
