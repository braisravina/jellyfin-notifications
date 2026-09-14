using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.Configuration;

/// <summary>
/// Thrown when a <see cref="PluginConfiguration"/> fails validation and cannot be saved.
/// </summary>
public class PluginConfigurationValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfigurationValidationException"/> class.
    /// </summary>
    /// <param name="errors">The validation error messages.</param>
    public PluginConfigurationValidationException(IReadOnlyList<string> errors)
        : base("Invalid Email Digest plugin configuration: " + string.Join(" ", errors))
    {
        Errors = errors;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfigurationValidationException"/> class.
    /// </summary>
    public PluginConfigurationValidationException()
        : base("Invalid Email Digest plugin configuration.")
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfigurationValidationException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public PluginConfigurationValidationException(string message)
        : base(message)
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfigurationValidationException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PluginConfigurationValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// Gets the individual validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }
}
