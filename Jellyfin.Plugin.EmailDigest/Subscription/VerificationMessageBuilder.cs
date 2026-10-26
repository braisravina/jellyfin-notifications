using System;
using System.Globalization;
using System.Net;
using System.Text;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Services;

namespace Jellyfin.Plugin.EmailDigest.Subscription;

/// <summary>
/// Builds the email that asks someone to confirm an address.
/// </summary>
/// <remarks>
/// It says nothing about media, libraries, user names or the server's contents: it goes to an address
/// that has not been proved to belong to the requester, so it must be safe in a stranger's inbox. It
/// states what was asked, how to confirm, when the link stops working, and that ignoring it changes
/// nothing. Everything inserted is HTML-encoded in the HTML part and kept on one line in the subject.
/// </remarks>
public sealed class VerificationMessageBuilder
{
    private readonly ILocalizationService _text;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerificationMessageBuilder"/> class.
    /// </summary>
    /// <param name="text">The localization service.</param>
    public VerificationMessageBuilder(ILocalizationService text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>
    /// Builds the message.
    /// </summary>
    /// <param name="recipientAddress">The address being verified.</param>
    /// <param name="language">The language to write in.</param>
    /// <param name="serverLabel">The sender display name (for example "Jellyfin"), used to say where the request came from.</param>
    /// <param name="link">The absolute verification link.</param>
    /// <param name="expiresUtc">When the link stops working.</param>
    /// <returns>The message, addressed to that one recipient.</returns>
    public OutgoingEmail Build(string recipientAddress, string? language, string serverLabel, string link, DateTimeOffset expiresUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(link);

        var label = string.IsNullOrWhiteSpace(serverLabel) ? "Jellyfin" : serverLabel.Trim();
        var subject = _text.GetString(language, "VerifySubject");
        var intro = string.Format(CultureInfo.InvariantCulture, _text.GetString(language, "VerifyIntro"), label);
        var action = _text.GetString(language, "VerifyAction");
        var button = _text.GetString(language, "VerifyButton");
        var expires = string.Format(CultureInfo.InvariantCulture, _text.GetString(language, "VerifyExpires"), expiresUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        var ignore = _text.GetString(language, "VerifyIgnore");
        var footer = _text.GetString(language, "VerifyFooter");

        var plain = new StringBuilder()
            .AppendLine(intro)
            .AppendLine()
            .AppendLine(action)
            .AppendLine(link)
            .AppendLine()
            .AppendLine(expires)
            .AppendLine(ignore)
            .AppendLine()
            .AppendLine(footer)
            .ToString();

        var html = new StringBuilder()
            .Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(Encode(subject)).Append("</title></head>")
            .Append("<body style=\"font-family:Arial,Helvetica,sans-serif;color:#222;max-width:560px;margin:0 auto;padding:16px\">")
            .Append("<p>").Append(Encode(intro)).Append("</p>")
            .Append("<p>").Append(Encode(action)).Append("</p>")
            .Append("<p><a href=\"").Append(Encode(link)).Append("\" style=\"display:inline-block;padding:10px 16px;background:#00a4dc;color:#fff;text-decoration:none;border-radius:4px\">")
            .Append(Encode(button)).Append("</a></p>")
            .Append("<p style=\"font-size:13px;color:#555\">").Append(Encode(link)).Append("</p>")
            .Append("<p>").Append(Encode(expires)).Append("</p>")
            .Append("<p>").Append(Encode(ignore)).Append("</p>")
            .Append("<p style=\"font-size:12px;color:#777\">").Append(Encode(footer)).Append("</p>")
            .Append("</body></html>")
            .ToString();

        return new OutgoingEmail(recipientAddress, null, subject, plain, html);
    }

    private static string Encode(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
