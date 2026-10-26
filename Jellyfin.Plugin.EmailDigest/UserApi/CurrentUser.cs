using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;

namespace Jellyfin.Plugin.EmailDigest.UserApi;

/// <summary>
/// Reads who is signed in from the claims Jellyfin's authentication built from the access token.
/// </summary>
/// <remarks>
/// This is the only place a user id for the user-facing API comes from. It is never read from a URL,
/// a query string, a body or a header the caller can choose. Jellyfin's own helper lets an
/// administrator pass another user's id; this plugin deliberately does not: an administrator who wants
/// to change notifications does so for themselves, here, and for the server in the administrator page.
/// An API key authenticates as an administrator with no user id and is refused: it is not a person.
/// </remarks>
public static class CurrentUser
{
    /// <summary>
    /// The claim carrying the signed-in user's id (format <c>N</c>), as set by Jellyfin's authentication.
    /// </summary>
    public const string UserIdClaim = "Jellyfin-UserId";

    /// <summary>
    /// The claim that is "True" when the request was authenticated with an API key.
    /// </summary>
    public const string ApiKeyClaim = "Jellyfin-IsApiKey";

    /// <summary>
    /// Gets the signed-in user's id.
    /// </summary>
    /// <param name="principal">The request's principal.</param>
    /// <returns>The user id, or null when the request is not from a signed-in person.</returns>
    public static Guid? From(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var apiKey = Claim(principal, ApiKeyClaim);
        if (bool.TryParse(apiKey, out var isApiKey) && isApiKey)
        {
            return null;
        }

        return Guid.TryParse(Claim(principal, UserIdClaim), CultureInfo.InvariantCulture, out var id) && id != Guid.Empty ? id : null;
    }

    private static string? Claim(ClaimsPrincipal principal, string type)
    {
        return principal.Claims.FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase))?.Value;
    }
}
