using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Subscription;
using Jellyfin.Plugin.EmailDigest.Verification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.UserApi;

/// <summary>
/// The two actions behind links in emails: confirming an address and unsubscribing. They need no
/// sign-in, because the recipient may open the message on a device that is not signed in to Jellyfin.
/// </summary>
/// <remarks>
/// <para>
/// This is the plugin's only anonymous surface, kept to two POST actions in one small class. The token
/// in the address is the whole credential. A token cannot be guessed (256 random bits, or an HMAC under a
/// server-held key), is never accepted from anywhere but the query, and does exactly one thing: confirm
/// that one pending address, or turn that one user's notifications off. Neither response contains an
/// address, a name, a preference or any other data, and an invalid token is indistinguishable from an
/// expired, used or forged one.
/// </para>
/// <para>
/// They act on POST only. A GET (which mail scanners and prefetchers make) never changes anything; the
/// pages that ask the person to press a button are served separately. The POST body is ignored, so the
/// one-click form <c>List-Unsubscribe=One-Click</c> that mail clients send (RFC 8058) works as it is.
/// Because the token is a secret nobody else holds, a forged cross-site request cannot succeed.
/// </para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Produces(MediaTypeNames.Application.Json)]
[SecurityHeaders]
public class EmailDigestLinkController : ControllerBase
{
    private readonly ISubscriptionService _service;
    private readonly ILogger<EmailDigestLinkController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailDigestLinkController"/> class.
    /// </summary>
    /// <param name="service">The subscription service.</param>
    /// <param name="logger">The logger.</param>
    public EmailDigestLinkController(ISubscriptionService service, ILogger<EmailDigestLinkController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Confirms the address a verification link was sent for.
    /// </summary>
    /// <param name="token">The token from the link.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>Confirmed, or a generic refusal.</returns>
    [HttpPost("EmailDigest/Verify")]
    public async Task<IActionResult> Verify([FromQuery] string? token, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await _service.ConfirmAddressAsync(token, cancellationToken).ConfigureAwait(false);
            return outcome switch
            {
                ConfirmOutcome.Confirmed => Ok(new { result = "confirmed" }),
                ConfirmOutcome.NotApplied => StatusCode(StatusCodes.Status409Conflict, new { error = "not-applied" }),
                _ => BadRequest(new { error = "invalid-link" })
            };
        }
        catch (System.Exception ex) when (ex is not System.OperationCanceledException)
        {
            _logger.LogError("An address confirmation failed ({ExceptionType}).", ex.GetType().Name);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "unexpected" });
        }
    }

    /// <summary>
    /// Turns off the notifications of the user an unsubscribe link was made for.
    /// </summary>
    /// <param name="token">The token from the link.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>Unsubscribed, or a generic refusal.</returns>
    [HttpPost("EmailDigest/Unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromQuery] string? token, CancellationToken cancellationToken)
    {
        try
        {
            return await _service.UnsubscribeAsync(token, cancellationToken).ConfigureAwait(false)
                ? Ok(new { result = "unsubscribed" })
                : BadRequest(new { error = "invalid-link" });
        }
        catch (System.Exception ex) when (ex is not System.OperationCanceledException)
        {
            _logger.LogError("An unsubscribe failed ({ExceptionType}).", ex.GetType().Name);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "unexpected" });
        }
    }
}
