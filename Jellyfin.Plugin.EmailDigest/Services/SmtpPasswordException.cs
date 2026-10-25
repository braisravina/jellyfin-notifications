using System;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Thrown when an SMTP password change is refused. The message never contains the password.
/// </summary>
public sealed class SmtpPasswordException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SmtpPasswordException"/> class.
    /// </summary>
    /// <param name="failure">Why the change was refused.</param>
    public SmtpPasswordException(SmtpPasswordFailure failure)
        : base("The SMTP password change was refused: " + failure)
    {
        Failure = failure;
    }

    /// <summary>
    /// Gets why the change was refused.
    /// </summary>
    public SmtpPasswordFailure Failure { get; }
}
