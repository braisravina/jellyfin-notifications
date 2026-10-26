using System;
using System.Globalization;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Subscription;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.UserApi;

/// <summary>
/// The signed-in user's own notification settings.
/// </summary>
/// <remarks>
/// <para>
/// Every action requires an authenticated Jellyfin user (<c>[Authorize]</c>, Jellyfin's default policy:
/// not administrator-only, because every user manages their own notifications). The user acted on is
/// <see cref="CurrentUser"/>, read from the authenticated claims; there is no user id in any route,
/// query or body, so one user cannot name another. An API key, a request without a user, and a
/// disabled or removed user are refused. Nothing here reaches SMTP settings, monitored libraries,
/// checkpoints or any other user's data: the service it calls has no such operations.
/// </para>
/// <para>
/// CSRF: Jellyfin authenticates API calls with a token the client sends in a header, never an ambient
/// cookie, so another site cannot make a browser send an authenticated request. As defence in depth every
/// change uses PUT, POST or DELETE and accepts only <c>application/json</c>, which a cross-site form
/// cannot send. Changes are rate limited per user. Unexpected errors produce a generic body.
/// </para>
/// </remarks>
[ApiController]
[Route("EmailDigest/Me")]
[Authorize]
[Produces(MediaTypeNames.Application.Json)]
[SecurityHeaders]
public class EmailDigestUserController : ControllerBase
{
    private readonly ISubscriptionService _service;
    private readonly UserRequestThrottle _throttle;
    private readonly ILogger<EmailDigestUserController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailDigestUserController"/> class.
    /// </summary>
    /// <param name="service">The subscription service.</param>
    /// <param name="throttle">The per-user change throttle.</param>
    /// <param name="logger">The logger.</param>
    public EmailDigestUserController(ISubscriptionService service, UserRequestThrottle throttle, ILogger<EmailDigestUserController> logger)
    {
        _service = service;
        _throttle = throttle;
        _logger = logger;
    }

    /// <summary>
    /// Gets the signed-in user's own overview.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The overview.</returns>
    [HttpGet]
    public Task<IActionResult> GetOverview(CancellationToken cancellationToken)
    {
        return RunAsync(false, async user =>
        {
            var overview = await _service.GetOverviewAsync(user, cancellationToken).ConfigureAwait(false);
            return overview is null ? Forbidden() : Ok(overview);
        });
    }

    /// <summary>
    /// Turns the user's digests on or off.
    /// </summary>
    /// <param name="request">The wish.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The new overview.</returns>
    [HttpPut("Notifications")]
    [Consumes(MediaTypeNames.Application.Json)]
    public Task<IActionResult> SetNotifications([FromBody] NotificationsRequest? request, CancellationToken cancellationToken)
    {
        return RunAsync(true, async user =>
        {
            if (request?.Enabled is null)
            {
                return Error(StatusCodes.Status400BadRequest, "invalid-value");
            }

            return await AfterChangeAsync(user, await _service.SetNotificationsAsync(user, request.Enabled.Value, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Changes the language of the user's digests and emails.
    /// </summary>
    /// <param name="request">The language.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The new overview.</returns>
    [HttpPut("Language")]
    [Consumes(MediaTypeNames.Application.Json)]
    public Task<IActionResult> SetLanguage([FromBody] LanguageRequest? request, CancellationToken cancellationToken)
    {
        return RunAsync(true, async user =>
            await AfterChangeAsync(user, await _service.SetLanguageAsync(user, request?.Language, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Asks to use a new notification address: a one-time confirmation link is emailed to it. The
    /// address in use does not change until the link is opened.
    /// </summary>
    /// <param name="request">The address.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>Whether the email was sent.</returns>
    [HttpPost("Address")]
    [Consumes(MediaTypeNames.Application.Json)]
    public Task<IActionResult> RequestAddress([FromBody] AddressRequest? request, CancellationToken cancellationToken)
    {
        return RunAsync(true, async user =>
        {
            var result = await _service.RequestAddressAsync(user, request?.Address, cancellationToken).ConfigureAwait(false);
            switch (result.Status)
            {
                case AddressRequestStatus.Sent:
                    return StatusCode(StatusCodes.Status202Accepted, new { status = "sent", expiresUtc = result.ExpiresUtc });
                case AddressRequestStatus.SentUncertain:
                    return StatusCode(StatusCodes.Status202Accepted, new { status = "uncertain", expiresUtc = result.ExpiresUtc });
                case AddressRequestStatus.InvalidAddress:
                    return Error(StatusCodes.Status400BadRequest, "invalid-address");
                case AddressRequestStatus.NotAvailable:
                    return Error(StatusCodes.Status409Conflict, "unavailable");
                case AddressRequestStatus.AlreadyVerified:
                    return Error(StatusCodes.Status409Conflict, "already-verified");
                case AddressRequestStatus.RateLimited:
                    return TooMany(result.RetryAfter ?? TimeSpan.FromMinutes(1));
                case AddressRequestStatus.SendFailed:
                    return Error(StatusCodes.Status502BadGateway, "send-failed");
                case AddressRequestStatus.UserUnavailable:
                    return Forbidden();
                default:
                    return Error(StatusCodes.Status503ServiceUnavailable, "storage");
            }
        });
    }

    /// <summary>
    /// Withdraws the user's outstanding address request.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The new overview.</returns>
    [HttpDelete("Address/Pending")]
    public Task<IActionResult> CancelPendingAddress(CancellationToken cancellationToken)
    {
        return RunAsync(true, async user =>
        {
            await _service.CancelPendingAddressAsync(user, cancellationToken).ConfigureAwait(false);
            var overview = await _service.GetOverviewAsync(user, cancellationToken).ConfigureAwait(false);
            return overview is null ? Forbidden() : Ok(overview);
        });
    }

    private async Task<IActionResult> AfterChangeAsync(Guid user, SubscriptionChange change, CancellationToken cancellationToken)
    {
        switch (change)
        {
            case SubscriptionChange.Saved:
                var overview = await _service.GetOverviewAsync(user, cancellationToken).ConfigureAwait(false);
                return overview is null ? Forbidden() : Ok(overview);
            case SubscriptionChange.InvalidValue:
                return Error(StatusCodes.Status400BadRequest, "invalid-value");
            case SubscriptionChange.UserUnavailable:
                return Forbidden();
            default:
                return Error(StatusCodes.Status503ServiceUnavailable, "storage");
        }
    }

    private async Task<IActionResult> RunAsync(bool isChange, Func<Guid, Task<IActionResult>> action)
    {
        var user = CurrentUser.From(User);
        if (user is null)
        {
            // Authenticated, but not as a person (for example an API key): not allowed here.
            return Forbidden();
        }

        if (isChange && !_throttle.TryAcquire(user.Value, out var retryAfter))
        {
            return TooMany(retryAfter);
        }

        try
        {
            return await action(user.Value).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            // Only the type is logged and nothing is returned: messages can quote addresses.
            _logger.LogError("An Email Digest user request failed ({ExceptionType}).", ex.GetType().Name);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "unexpected" });
        }
    }

    private ObjectResult Error(int status, string code)
    {
        return StatusCode(status, new { error = code });
    }

    private ObjectResult Forbidden()
    {
        return Error(StatusCodes.Status403Forbidden, "forbidden");
    }

    private ObjectResult TooMany(TimeSpan retryAfter)
    {
        var seconds = (int)Math.Ceiling(Math.Max(1, retryAfter.TotalSeconds));
        Response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
        return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "rate-limited", retryAfterSeconds = seconds });
    }
}
