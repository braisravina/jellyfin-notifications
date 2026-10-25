using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Builds the deterministic Message-ID local part for one user's digest.
/// </summary>
/// <remarks>
/// Derived only from who the digest is for, the window it covers and the items in it, so a resend of
/// exactly the same digest (for example after an uncertain outcome) carries the same id, while a
/// different digest does not. It is a diagnostic aid: nothing assumes a mail provider deduplicates
/// on it. The id reveals nothing: it is a one-way hash.
/// </remarks>
public static class DigestMessageId
{
    /// <summary>
    /// Creates the local part.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <param name="window">The window the digest covers.</param>
    /// <param name="itemIds">The ids of the items the digest delivers.</param>
    /// <returns>A 44-character id such as <c>emaildigest-0123456789abcdef0123456789abcdef</c>.</returns>
    public static string Create(Guid userId, DeliveryWindow window, IEnumerable<Guid> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        var text = new StringBuilder()
            .Append(userId.ToString("N")).Append('|')
            .Append(window.FromUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(window.ToUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)).Append('|');
        foreach (var id in itemIds.OrderBy(i => i))
        {
            text.Append(id.ToString("N")).Append(',');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return "emaildigest-" + Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }
}
