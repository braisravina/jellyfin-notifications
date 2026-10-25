namespace Jellyfin.Plugin.EmailDigest.Email;

/// <summary>
/// One already-rendered message for exactly one recipient.
/// </summary>
/// <remarks>
/// There is no list of recipients and no Cc or Bcc by construction: a message can only ever go to
/// the one address given, so one user's address can never reach another user.
/// </remarks>
/// <param name="RecipientAddress">The single recipient address.</param>
/// <param name="RecipientName">The recipient's display name, or null.</param>
/// <param name="Subject">The single-line subject.</param>
/// <param name="TextBody">The plain-text body.</param>
/// <param name="HtmlBody">The HTML body.</param>
public sealed record OutgoingEmail(string RecipientAddress, string? RecipientName, string Subject, string TextBody, string HtmlBody);
