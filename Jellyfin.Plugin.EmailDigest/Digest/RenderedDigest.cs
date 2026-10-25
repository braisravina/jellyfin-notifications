namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// A finished, localized digest message. Pure data: nothing has been sent.
/// </summary>
/// <param name="Subject">A single-line subject.</param>
/// <param name="HtmlBody">A complete, self-contained HTML document.</param>
/// <param name="TextBody">The same content as plain text, with <c>\n</c> line endings.</param>
public sealed record RenderedDigest(string Subject, string HtmlBody, string TextBody);
