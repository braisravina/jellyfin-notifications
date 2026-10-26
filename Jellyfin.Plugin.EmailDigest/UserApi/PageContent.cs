using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.EmailDigest.UserApi;

/// <summary>
/// The embedded page, ready to serve, with the Content-Security-Policy that goes with it.
/// </summary>
/// <remarks>
/// One static page serves the preferences screen and the two confirmation screens; only a fixed word in
/// the body tells it which. The policy allows nothing but the page's own inline script and style, named by
/// their SHA-256 hashes, and requests back to the same server: no other script, no frames, no forms, no
/// images, no outside connections, so even a flaw in the page could not load code or send data elsewhere.
/// </remarks>
public static partial class PageContent
{
    private static readonly string Template = Load();

    /// <summary>
    /// Gets the page for a screen.
    /// </summary>
    /// <param name="mode">"me", "verify" or "unsubscribe"; anything else is refused.</param>
    /// <returns>The HTML.</returns>
    public static string Html(string mode)
    {
        if (mode is not ("me" or "verify" or "unsubscribe"))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return Template.Replace("{{MODE}}", mode, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the Content-Security-Policy for the page.
    /// </summary>
    /// <returns>The header value.</returns>
    public static string Policy()
    {
        return "default-src 'none'; "
               + "script-src '" + Hash(InnerText(ScriptPattern())) + "'; "
               + "style-src '" + Hash(InnerText(StylePattern())) + "'; "
               + "connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; img-src 'none'";
    }

    /// <summary>
    /// Gets the page template, for tests that inspect it.
    /// </summary>
    /// <returns>The raw HTML with its placeholder.</returns>
    internal static string Source() => Template;

    private static string Load()
    {
        var assembly = typeof(PageContent).Assembly;
        var name = typeof(PageContent).Namespace + ".Pages.page.html";
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("The page resource is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string InnerText(Regex pattern)
    {
        var match = pattern.Match(Template);
        return match.Success ? match.Groups[1].Value : throw new InvalidOperationException("The page has no such block.");
    }

    private static string Hash(string text)
    {
        return "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    [GeneratedRegex("<script>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex ScriptPattern();

    [GeneratedRegex("<style>(.*?)</style>", RegexOptions.Singleline)]
    private static partial Regex StylePattern();
}
