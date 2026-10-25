using Jellyfin.Plugin.EmailDigest.Email;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Maps transport results to a <see cref="DeliveryFailureKind"/>.
/// </summary>
public static class DeliveryFailureClassifier
{
    /// <summary>
    /// Classifies a result.
    /// </summary>
    /// <param name="result">The transport result.</param>
    /// <returns>The kind.</returns>
    public static DeliveryFailureKind Classify(EmailTransportResult result)
    {
        var temporaryReply = result.SmtpStatusCode is >= 400 and < 500;
        return result.Outcome switch
        {
            EmailTransportOutcome.Accepted => DeliveryFailureKind.Accepted,
            EmailTransportOutcome.ConnectionFailed or EmailTransportOutcome.Timeout => DeliveryFailureKind.Transient,
            EmailTransportOutcome.RecipientRejected => temporaryReply ? DeliveryFailureKind.Transient : DeliveryFailureKind.PermanentForUser,
            EmailTransportOutcome.SenderRejected => temporaryReply ? DeliveryFailureKind.Transient : DeliveryFailureKind.StopRun,
            EmailTransportOutcome.AuthenticationFailed or EmailTransportOutcome.TlsFailed => DeliveryFailureKind.StopRun,
            EmailTransportOutcome.Uncertain => DeliveryFailureKind.Uncertain,
            EmailTransportOutcome.Cancelled => DeliveryFailureKind.Cancelled,
            EmailTransportOutcome.InvalidRequest => DeliveryFailureKind.PermanentForUser,
            _ => DeliveryFailureKind.PermanentForUser
        };
    }
}
