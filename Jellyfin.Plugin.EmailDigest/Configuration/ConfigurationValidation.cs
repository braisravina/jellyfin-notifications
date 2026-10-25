using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.EmailDigest.Configuration;

/// <summary>
/// Small, dependency-free validators shared by global configuration and per-user preferences,
/// so the two can never validate email addresses or language codes differently.
/// </summary>
public static partial class ConfigurationValidation
{
    /// <summary>
    /// Gets the smallest accepted SMTP timeout, in seconds.
    /// </summary>
    public const int MinTimeoutSeconds = 5;

    /// <summary>
    /// Gets the largest accepted SMTP timeout, in seconds.
    /// </summary>
    public const int MaxTimeoutSeconds = 120;

    /// <summary>
    /// The largest accepted fallback lookback, in hours (30 days), equal to the longest span any
    /// single digest reaches back.
    /// </summary>
    public const int MaxLookbackHours = 720;

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

    /// <summary>
    /// Checks whether a string is a plausible host name or IP address.
    /// </summary>
    /// <param name="host">The candidate host.</param>
    /// <returns><see langword="true"/> for a DNS host name or an IP address literal.</returns>
    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253)
        {
            return false;
        }

        return System.Net.IPAddress.TryParse(host, out _) || HostPattern().IsMatch(host);
    }

    /// <summary>
    /// Checks that a value has no control characters (so no line breaks) and a bounded length.
    /// </summary>
    /// <param name="value">The value; null counts as empty and is accepted.</param>
    /// <param name="maxLength">The maximum length.</param>
    /// <returns><see langword="true"/> when the value is a single line within the limit.</returns>
    public static bool IsValidSingleLine(string? value, int maxLength)
    {
        return value is null || (value.Length <= maxLength && !value.Any(char.IsControl));
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$")]
    private static partial Regex HostPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
