using System;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.EmailDigest.UserApi;

/// <summary>
/// Adds the headers every response of the user-facing endpoints must carry.
/// </summary>
/// <remarks>
/// Responses are never cached (they are personal, and links in them carry one-time tokens), never
/// content-sniffed, and the referrer is withheld so a token in a page's address cannot leak to anything
/// the page links to.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SecurityHeadersAttribute : ResultFilterAttribute
{
    /// <inheritdoc />
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var headers = context.HttpContext.Response.Headers;
        headers["Cache-Control"] = "no-store";
        headers["Pragma"] = "no-cache";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        base.OnResultExecuting(context);
    }
}
