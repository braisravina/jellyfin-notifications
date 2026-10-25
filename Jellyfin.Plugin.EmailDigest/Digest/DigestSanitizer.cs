using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// Cleans untrusted metadata (titles, names) before it is placed in a message.
/// </summary>
/// <remarks>
/// Encoding for the output format is a separate, later step. This removes what no encoder can make
/// safe or sensible: control characters (so a title cannot start a new line in plain text or a
/// header), and bidirectional override characters (so it cannot reorder surrounding text). Runs of
/// whitespace collapse to one space, and very long values are cut.
/// </remarks>
internal static class DigestSanitizer
{
    private const int MaxLength = 200;

    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var rune in value.EnumerateRunes())
        {
            if (IsBidiControl(rune.Value))
            {
                continue;
            }

            if (Rune.IsWhiteSpace(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(rune.ToString());
        }

        var cleaned = builder.ToString();
        if (new StringInfo(cleaned).LengthInTextElements <= MaxLength)
        {
            return cleaned;
        }

        var elements = StringInfo.GetTextElementEnumerator(cleaned);
        var end = 0;
        var count = 0;
        while (count < MaxLength - 1 && elements.MoveNext())
        {
            end = elements.ElementIndex + elements.GetTextElement().Length;
            count++;
        }

        return cleaned[..end] + "…";
    }

    private static bool IsBidiControl(int value)
    {
        return value is 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069);
    }
}
