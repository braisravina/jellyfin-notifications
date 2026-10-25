namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// The result of a connection or test-email attempt, safe to show.
/// </summary>
/// <param name="Outcome">The outcome code, for example "Accepted" or "TlsFailed".</param>
/// <param name="Category">"ok", "configuration", "connectivity", "tls", "authentication", "recipient" or "other".</param>
/// <param name="Message">A localized explanation containing no server text, addresses or secrets.</param>
/// <param name="SmtpStatusCode">The SMTP reply code, when one explains the outcome.</param>
public sealed record TransportResultDto(string Outcome, string Category, string Message, int? SmtpStatusCode);
