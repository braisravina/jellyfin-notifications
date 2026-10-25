namespace Jellyfin.Plugin.EmailDigest.Email;

/// <summary>
/// The structured, safe-to-show result of a transport operation.
/// </summary>
/// <remarks>
/// Deliberately carries no free text: server banners, exception messages and addresses can contain
/// private information and are never copied here. Callers localize the outcome themselves.
/// </remarks>
/// <param name="Outcome">What happened.</param>
/// <param name="SmtpStatusCode">The SMTP reply code, when the server sent one that explains the outcome.</param>
public readonly record struct EmailTransportResult(EmailTransportOutcome Outcome, int? SmtpStatusCode = null)
{
    /// <summary>
    /// Gets a value indicating whether the server accepted the request.
    /// </summary>
    public bool IsAccepted => Outcome == EmailTransportOutcome.Accepted;
}
