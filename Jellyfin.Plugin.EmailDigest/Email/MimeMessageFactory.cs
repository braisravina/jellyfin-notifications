using System;
using System.Linq;
using MimeKit;
using MimeKit.Text;

namespace Jellyfin.Plugin.EmailDigest.Email;

/// <summary>
/// Builds the MIME message for one <see cref="OutgoingEmail"/>.
/// </summary>
/// <remarks>
/// The structure is multipart/alternative with the plain-text part first and the HTML part second
/// (clients show the last alternative they support). Both parts are UTF-8; MimeKit chooses
/// quoted-printable or base64 as needed and encodes the subject and display names per RFC 2047.
/// There is exactly one To mailbox and no Cc or Bcc. Header values are validated to be single lines.
/// </remarks>
public static class MimeMessageFactory
{
    /// <summary>
    /// Builds the message.
    /// </summary>
    /// <param name="settings">The settings supplying the sender.</param>
    /// <param name="email">The message content and recipient.</param>
    /// <returns>The MIME message.</returns>
    /// <exception cref="ArgumentException">An address or header value is invalid.</exception>
    public static MimeMessage Create(SmtpSettings settings, OutgoingEmail email)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(email);

        var sender = ParseMailbox(settings.SenderName, settings.SenderAddress, "sender");
        var recipient = ParseMailbox(email.RecipientName, email.RecipientAddress, "recipient");

        if (string.IsNullOrWhiteSpace(email.Subject) || HasLineBreak(email.Subject))
        {
            throw new ArgumentException("The subject must be a single non-empty line.", nameof(email));
        }

        var message = new MimeMessage();
        message.From.Add(sender);
        message.To.Add(recipient);
        message.Subject = email.Subject;
        message.Date = DateTimeOffset.UtcNow;
        if (email.MessageIdLocalPart is null)
        {
            message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(sender.Domain);
        }
        else if (IsValidMessageIdPart(email.MessageIdLocalPart) && !string.IsNullOrEmpty(sender.Domain))
        {
            message.MessageId = email.MessageIdLocalPart + "@" + sender.Domain;
        }
        else
        {
            throw new ArgumentException("The message id is invalid.", nameof(email));
        }

        // Tells well-behaved auto-responders (out-of-office, vacation) not to answer a robot.
        message.Headers.Add("Auto-Submitted", "auto-generated");

        if (email.UnsubscribeUrl is not null)
        {
            if (!IsSafeUnsubscribeUrl(email.UnsubscribeUrl))
            {
                throw new ArgumentException("The unsubscribe link is invalid.", nameof(email));
            }

            message.Headers.Add("List-Unsubscribe", "<" + email.UnsubscribeUrl + ">");
            message.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }

        var text = new TextPart(TextFormat.Plain) { Text = email.TextBody };
        text.ContentType.Charset = "utf-8";
        var html = new TextPart(TextFormat.Html) { Text = email.HtmlBody };
        html.ContentType.Charset = "utf-8";

        var alternative = new MultipartAlternative { text, html };
        message.Body = alternative;
        return message;
    }

    private static MailboxAddress ParseMailbox(string? name, string address, string role)
    {
        if (string.IsNullOrWhiteSpace(address) || HasLineBreak(address) || (name is not null && HasLineBreak(name)))
        {
            throw new ArgumentException($"The {role} address or name is invalid.", role);
        }

        // MimeKit accepts a bare local part such as "root" as an address; a digest recipient must be
        // a full user@domain address, so the plugin's own check applies as well.
        if (!MailboxAddress.TryParse(address, out var parsed)
            || !Configuration.ConfigurationValidation.IsValidEmail(parsed.Address))
        {
            throw new ArgumentException($"The {role} address is invalid.", role);
        }

        return new MailboxAddress(string.IsNullOrWhiteSpace(name) ? null : name, parsed.Address);
    }

    private static bool IsValidMessageIdPart(string value)
    {
        return value.Length is >= 8 and <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    }

    // The value goes inside <...> in a header, so it must be a plain absolute http(s) address.
    private static bool IsSafeUnsubscribeUrl(string url)
    {
        return url.Length <= 600
               && !url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '<' or '>' or '"' or ',')
               && Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https";
    }

    private static bool HasLineBreak(string value)
    {
        return value.Any(char.IsControl);
    }
}
