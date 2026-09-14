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

        if (configuration.FallbackLookbackHours < 0)
        {
            errors.Add("FallbackLookbackHours cannot be negative.");
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
