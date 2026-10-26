using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// Writes a <see cref="DigestView"/> as plain text with <c>\n</c> line endings.
/// </summary>
internal static class DigestTextWriter
{
    public static string Write(DigestView view)
    {
        var text = new StringBuilder();
        text.Append(view.Greeting).Append("\n\n");

        if (view.EmptyMessage is not null)
        {
            text.Append(view.EmptyMessage).Append('\n');
        }
        else
        {
            text.Append(view.Intro).Append('\n');
        }

        foreach (var section in view.Sections)
        {
            text.Append('\n').Append(section.Heading).Append('\n');
            text.Append('-', new StringInfo(section.Heading).LengthInTextElements).Append('\n');

            foreach (var entry in section.Entries)
            {
                text.Append("* ").Append(entry.Title);
                if (entry.Detail is not null)
                {
                    text.Append(" — ").Append(entry.Detail);
                }

                text.Append('\n');

                foreach (var line in entry.SubLines)
                {
                    text.Append("    - ").Append(line).Append('\n');
                }

                if (entry.More is not null)
                {
                    text.Append("    ").Append(entry.More).Append('\n');
                }
            }

            if (section.Overflow is not null)
            {
                text.Append("  ").Append(section.Overflow).Append('\n');
            }
        }

        if (view.ContinuationNote is not null)
        {
            text.Append('\n').Append(view.ContinuationNote).Append('\n');
        }

        text.Append("\n--\n").Append(view.Footer).Append('\n');
        foreach (var link in view.Links)
        {
            text.Append(link.Label).Append(": ").Append(link.Url).Append('\n');
        }

        return text.ToString();
    }
}
