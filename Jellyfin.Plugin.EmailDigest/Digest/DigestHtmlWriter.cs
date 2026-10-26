using System.Net;
using System.Text;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// Writes a <see cref="DigestView"/> as a self-contained, email-safe HTML document.
/// </summary>
/// <remarks>
/// Every dynamic value goes through <see cref="Encode"/>; the only literal markup is the fixed
/// template below. Layout uses presentation tables (the email-client baseline) around semantic
/// headings and lists, inline CSS only, a fluid width (max 600px) that shrinks on narrow screens,
/// and fixed colours. No scripts, links, images, fonts, forms or external resources.
/// </remarks>
internal static class DigestHtmlWriter
{
    private const string Font = "font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;";

    public static string Write(DigestView view)
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n");
        html.Append("<html lang=\"").Append(Encode(view.Language)).Append("\">\n<head>\n");
        html.Append("<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<title>").Append(Encode(view.Subject)).Append("</title>\n</head>\n");
        html.Append("<body style=\"margin:0;padding:0;background-color:#f4f5f7;\">\n");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background-color:#f4f5f7;\">\n");
        html.Append("<tr><td align=\"center\" style=\"padding:16px;\">\n");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"max-width:600px;background-color:#ffffff;border-radius:8px;\">\n");
        html.Append("<tr><td style=\"padding:24px;").Append(Font).Append("color:#1f2933;font-size:16px;line-height:1.5;\">\n");

        html.Append("<h1 style=\"margin:0 0 12px 0;font-size:22px;line-height:1.3;\">").Append(Encode(view.Greeting)).Append("</h1>\n");
        html.Append("<p style=\"margin:0 0 16px 0;\">").Append(Encode(view.EmptyMessage ?? view.Intro)).Append("</p>\n");

        foreach (var section in view.Sections)
        {
            html.Append("<h2 style=\"margin:24px 0 8px 0;font-size:18px;line-height:1.3;border-bottom:1px solid #d9dde3;padding-bottom:4px;\">")
                .Append(Encode(section.Heading))
                .Append("</h2>\n");

            if (section.Entries.Count > 0)
            {
                html.Append("<ul style=\"margin:0 0 8px 0;padding-left:20px;\">\n");
                foreach (var entry in section.Entries)
                {
                    html.Append("<li style=\"margin:0 0 6px 0;\"><strong>").Append(Encode(entry.Title)).Append("</strong>");
                    if (entry.Detail is not null)
                    {
                        html.Append(" <span style=\"color:#52606d;\">— ").Append(Encode(entry.Detail)).Append("</span>");
                    }

                    if (entry.SubLines.Count > 0 || entry.More is not null)
                    {
                        html.Append("\n<ul style=\"margin:4px 0 0 0;padding-left:20px;font-size:14px;color:#3e4c59;\">\n");
                        foreach (var line in entry.SubLines)
                        {
                            html.Append("<li>").Append(Encode(line)).Append("</li>\n");
                        }

                        if (entry.More is not null)
                        {
                            html.Append("<li style=\"list-style:none;margin-left:-20px;color:#52606d;font-style:italic;\">").Append(Encode(entry.More)).Append("</li>\n");
                        }

                        html.Append("</ul>\n");
                    }

                    html.Append("</li>\n");
                }

                html.Append("</ul>\n");
            }

            if (section.Overflow is not null)
            {
                html.Append("<p style=\"margin:0 0 8px 0;color:#52606d;font-style:italic;\">").Append(Encode(section.Overflow)).Append("</p>\n");
            }
        }

        if (view.ContinuationNote is not null)
        {
            html.Append("<p style=\"margin:16px 0 0 0;font-size:14px;color:#3e4c59;\">").Append(Encode(view.ContinuationNote)).Append("</p>\n");
        }

        html.Append("<hr style=\"border:0;border-top:1px solid #d9dde3;margin:24px 0 12px 0;\">\n");
        html.Append("<p style=\"margin:0;font-size:12px;color:#7b8794;\">").Append(Encode(view.Footer)).Append("</p>\n");
        foreach (var link in view.Links)
        {
            html.Append("<p style=\"margin:4px 0 0 0;font-size:12px;color:#7b8794;\"><a href=\"").Append(Encode(link.Url)).Append("\" style=\"color:#7b8794;\">").Append(Encode(link.Label)).Append("</a></p>\n");
        }

        html.Append("</td></tr>\n</table>\n</td></tr>\n</table>\n</body>\n</html>\n");
        return html.ToString();
    }

    private static string Encode(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
