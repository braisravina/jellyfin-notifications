using System.Linq;
using System.Net.Mime;
using Jellyfin.Plugin.EmailDigest.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.EmailDigest.UserApi;

/// <summary>
/// The pages people open from Jellyfin or from an email, and the strings they show.
/// </summary>
/// <remarks>
/// <para>
/// Anonymous on purpose, because Jellyfin's web client keeps plugin pages behind an administrator check
/// and these must work for ordinary users and for someone opening an email on a device that is not signed
/// in. They are safe to serve to anyone: the HTML holds no data, only code that, once the person is signed
/// in, calls the authenticated API with the person's own token. Opening a page changes nothing; a button
/// press does, through the POST actions.
/// </para>
/// <para>
/// Every response is no-store, nosniff, no-referrer (the address of a confirmation page carries a one-time
/// token), cannot be framed, and carries a Content-Security-Policy that allows only the page's own script
/// and style by hash. The strings are a fixed list of interface text.
/// </para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[SecurityHeaders]
public class EmailDigestPageController : ControllerBase
{
    private readonly ILocalizationService _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailDigestPageController"/> class.
    /// </summary>
    /// <param name="localization">The localization service.</param>
    public EmailDigestPageController(ILocalizationService localization)
    {
        _localization = localization;
    }

    /// <summary>
    /// Gets the signed-in user's preferences page.
    /// </summary>
    /// <returns>The page.</returns>
    [HttpGet("EmailDigest/Me/Page")]
    public IActionResult PreferencesPage() => Page("me");

    /// <summary>
    /// Gets the page that asks a person to press a button to confirm an address.
    /// </summary>
    /// <returns>The page.</returns>
    [HttpGet("EmailDigest/Verify")]
    public IActionResult VerifyPage() => Page("verify");

    /// <summary>
    /// Gets the page that asks a person to press a button to stop digest emails.
    /// </summary>
    /// <returns>The page.</returns>
    [HttpGet("EmailDigest/Unsubscribe")]
    public IActionResult UnsubscribePage() => Page("unsubscribe");

    /// <summary>
    /// Gets the interface text for the pages.
    /// </summary>
    /// <param name="language">en, es or gl; anything else gives English.</param>
    /// <returns>The language used and a key-to-text map.</returns>
    [HttpGet("EmailDigest/Strings")]
    [Produces(MediaTypeNames.Application.Json)]
    public IActionResult GetStrings([FromQuery] string? language)
    {
        var text = new Digest.DigestText(_localization, language);
        return Ok(new { language = text.Language, strings = UserStringKeys.All.ToDictionary(key => key, key => text.Format(key), System.StringComparer.Ordinal) });
    }

    private ContentResult Page(string mode)
    {
        Response.Headers["Content-Security-Policy"] = PageContent.Policy();
        Response.Headers["X-Frame-Options"] = "DENY";
        return Content(PageContent.Html(mode), "text/html; charset=utf-8");
    }
}
