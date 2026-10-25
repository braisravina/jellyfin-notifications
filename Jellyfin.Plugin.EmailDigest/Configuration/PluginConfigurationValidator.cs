using System;
using System.Collections.Generic;
using Jellyfin.Plugin.EmailDigest.Localization;

namespace Jellyfin.Plugin.EmailDigest.Configuration;

/// <summary>
/// Validates a <see cref="PluginConfiguration"/> before it is persisted.
/// </summary>
public static class PluginConfigurationValidator
{
    /// <summary>
    /// Validates the given configuration.
    /// </summary>
    /// <param name="configuration">The configuration to validate.</param>
    /// <returns>A list of human-readable validation error messages; empty if valid.</returns>
    public static IReadOnlyList<string> Validate(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = new List<string>();

        if (!ConfigurationValidation.IsValidPort(configuration.SmtpPort))
        {
            errors.Add("SmtpPort must be between 1 and 65535.");
        }

        if (!Enum.IsDefined(configuration.SmtpSecurity))
        {
            errors.Add("SmtpSecurity is not a recognized value.");
        }

        if (configuration.SmtpTimeoutSeconds is < ConfigurationValidation.MinTimeoutSeconds or > ConfigurationValidation.MaxTimeoutSeconds)
        {
            errors.Add($"SmtpTimeoutSeconds must be between {ConfigurationValidation.MinTimeoutSeconds} and {ConfigurationValidation.MaxTimeoutSeconds}.");
        }

        if (!string.IsNullOrEmpty(configuration.SmtpHost) && !ConfigurationValidation.IsValidHost(configuration.SmtpHost))
        {
            errors.Add("SmtpHost must be a valid host name or IP address.");
        }

        if (!string.IsNullOrEmpty(configuration.SmtpUsername) && !ConfigurationValidation.IsValidSingleLine(configuration.SmtpUsername, 256))
        {
            errors.Add("SmtpUsername must be a single line of at most 256 characters.");
        }

        if (!string.IsNullOrEmpty(configuration.SmtpUsername) && configuration.SmtpSecurity == SmtpSecurityMode.None)
        {
            errors.Add("SmtpUsername cannot be used with unencrypted SMTP: credentials would be sent in clear text. Choose STARTTLS or implicit TLS.");
        }

        if (!ConfigurationValidation.IsValidSingleLine(configuration.SenderDisplayName, 100))
        {
            errors.Add("SenderDisplayName must be a single line of at most 100 characters.");
        }

        if (configuration.SmtpEnabled)
        {
            if (string.IsNullOrWhiteSpace(configuration.SmtpHost))
            {
                errors.Add("SmtpHost is required when SMTP is enabled.");
            }

            if (!ConfigurationValidation.IsValidEmail(configuration.SenderEmailAddress))
            {
                errors.Add("SenderEmailAddress must be a valid email address when SMTP is enabled.");
            }
        }
        else if (!string.IsNullOrEmpty(configuration.SenderEmailAddress)
                 && !ConfigurationValidation.IsValidEmail(configuration.SenderEmailAddress))
        {
            errors.Add("SenderEmailAddress must be a valid email address.");
        }

        if (!SupportedLanguages.IsSupported(configuration.DefaultLanguage))
        {
            errors.Add($"DefaultLanguage '{configuration.DefaultLanguage}' is not a supported language code.");
        }

        if (configuration.FallbackLookbackHours is < 0 or > ConfigurationValidation.MaxLookbackHours)
        {
            errors.Add($"FallbackLookbackHours must be between 0 and {ConfigurationValidation.MaxLookbackHours}.");
        }

        if (configuration.MaxEntriesPerSection is < 1 or > 200)
        {
            errors.Add("MaxEntriesPerSection must be between 1 and 200.");
        }

        if (configuration.MaxEntriesPerDigest is < 1 or > 500)
        {
            errors.Add("MaxEntriesPerDigest must be between 1 and 500.");
        }

        if (configuration.MaxEpisodesPerSeries is < 1 or > 50)
        {
            errors.Add("MaxEpisodesPerSeries must be between 1 and 50.");
        }

        for (var i = 0; i < configuration.MonitoredLibraries.Count; i++)
        {
            var library = configuration.MonitoredLibraries[i];

            if (library.LibraryId == Guid.Empty)
            {
                errors.Add($"MonitoredLibraries[{i}].LibraryId must not be empty.");
            }

            if (!Enum.IsDefined(library.Category))
            {
                errors.Add($"MonitoredLibraries[{i}].Category is not a recognized value.");
            }
        }

        return errors;
    }
}
