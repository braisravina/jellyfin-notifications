using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// Administrator-only endpoints for SMTP settings, the SMTP password and SMTP tests.
/// </summary>
/// <remarks>
/// <para>
/// Every action requires Jellyfin's <c>RequiresElevation</c> policy (an authenticated administrator);
/// the policy is declared once on the class and there is no <c>AllowAnonymous</c> anywhere.
/// </para>
/// <para>
/// CSRF: Jellyfin authenticates API calls with a token the client sends in a request header, never
/// with an ambient cookie, so a third-party page cannot make a browser send an authenticated request.
/// As defence in depth, every action that changes state uses PUT, POST or DELETE (never GET) and the
/// ones with a body accept only <c>application/json</c>, which a cross-site form cannot send.
/// </para>
/// <para>
/// The password only ever travels in a request body, is never returned, and is never logged. Responses
/// are not cacheable. Unexpected errors produce a generic body, never an exception message.
/// </para>
/// </remarks>
[ApiController]
[Route("EmailDigest/Admin")]
[Authorize(Policy = Policies.RequiresElevation)]
[Produces(MediaTypeNames.Application.Json)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EmailDigestAdminController : ControllerBase
{
    private readonly SmtpAdminService _service;
    private readonly ILogger<EmailDigestAdminController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailDigestAdminController"/> class.
    /// </summary>
    /// <param name="service">The administrator service.</param>
    /// <param name="logger">The logger.</param>
    public EmailDigestAdminController(SmtpAdminService service, ILogger<EmailDigestAdminController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Gets the SMTP settings. The password is represented only by a status.
    /// </summary>
    /// <returns>The settings.</returns>
    [HttpGet("Settings")]
    public Task<IActionResult> GetSettings()
    {
        return RunAsync(() => Task.FromResult(_service.GetSettings()));
    }

    /// <summary>
    /// Changes the SMTP settings. The body cannot carry a password.
    /// </summary>
    /// <param name="update">The new settings.</param>
    /// <returns>The saved settings, or the validation errors.</returns>
    [HttpPut("Settings")]
    [Consumes(MediaTypeNames.Application.Json)]
    public Task<IActionResult> UpdateSettings([FromBody] SmtpSettingsUpdate? update)
    {
        return RunAsync(() => Task.FromResult(_service.UpdateSettings(update)));
    }

    /// <summary>
    /// Replaces the stored SMTP password. "Leave unchanged" is simply not calling this.
    /// </summary>
    /// <param name="update">The new password, in the body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The password status; never the password.</returns>
    [HttpPut("Password")]
    [Consumes(MediaTypeNames.Application.Json)]
    public Task<IActionResult> SetPassword([FromBody] SmtpPasswordUpdate? update, CancellationToken cancellationToken)
    {
        return RunAsync(() => _service.SetPasswordAsync(update, cancellationToken));
    }

    /// <summary>
    /// Removes the stored SMTP password.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The password status.</returns>
    [HttpDelete("Password")]
    public Task<IActionResult> ClearPassword(CancellationToken cancellationToken)
    {
        return RunAsync(() => _service.ClearPasswordAsync(cancellationToken));
    }

    /// <summary>
    /// Tests the saved settings without sending any message.
    /// </summary>
    /// <param name="language">The language for the explanation.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The outcome.</returns>
    [HttpPost("TestConnection")]
    public Task<IActionResult> TestConnection([FromQuery] string? language, CancellationToken cancellationToken)
    {
        return RunAsync(() => _service.TestConnectionAsync(language, cancellationToken));
    }

    /// <summary>
    /// Sends one test message to an explicitly chosen address.
    /// </summary>
    /// <param name="request">The destination.</param>
    /// <param name="language">The language for the message and explanation.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The outcome.</returns>
    [HttpPost("TestEmail")]
    [Consumes(MediaTypeNames.Application.Json)]
    public Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest? request, [FromQuery] string? language, CancellationToken cancellationToken)
    {
        return RunAsync(() => _service.SendTestEmailAsync(request, language, cancellationToken));
    }

    /// <summary>
    /// Gets the administrator-page strings.
    /// </summary>
    /// <param name="language">The requested language.</param>
    /// <returns>The strings.</returns>
    [HttpGet("Strings")]
    public Task<IActionResult> GetStrings([FromQuery] string? language)
    {
        return RunAsync(() => Task.FromResult(_service.GetStrings(language)));
    }

    private async Task<IActionResult> RunAsync(Func<Task<AdminResponse>> action)
    {
        try
        {
            var response = await action().ConfigureAwait(false);
            return StatusCode(response.StatusCode, response.Body);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            // Only the type is logged and nothing is returned: messages can quote settings or secrets.
            _logger.LogError("An Email Digest administrator request failed ({ExceptionType}).", ex.GetType().Name);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "unexpected" });
        }
    }
}
