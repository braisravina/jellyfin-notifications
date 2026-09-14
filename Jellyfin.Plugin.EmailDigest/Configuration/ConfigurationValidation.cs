using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.EmailDigest.Configuration;

/// <summary>
/// Small, dependency-free validators shared by global configuration and per-user preferences,
/// so the two can never validate email addresses or language codes differently.
/// </summary>
public static partial class ConfigurationValidation
{
    /// <summary>
    /// Checks whether a string is a plausible email address.
    /// </summary>
    /// <remarks>
    /// This is a sanity check on admin/user input, not a security control or a deliverability
    /// guarantee. It intentionally stays simple rather than attempting full RFC 5322 compliance.
    /// </remarks>
    /// <param name="email">The candidate email address.</param>
    /// <returns><see langword="true"/> if it looks like a valid email address.</returns>
    public static bool IsValidEmail(string? email)
    {
        return !string.IsNullOrWhiteSpace(email) && EmailPattern().IsMatch(email);
    }

    /// <summary>
    /// Checks whether a port number is in the valid TCP port range.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <returns><see langword="true"/> if <paramref name="port"/> is between 1 and 65535 inclusive.</returns>
    public static bool IsValidPort(int port)
    {
        return port is > 0 and <= 65535;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
