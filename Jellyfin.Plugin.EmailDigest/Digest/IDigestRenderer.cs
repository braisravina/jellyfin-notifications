namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// Renders a digest model as a localized subject, HTML body and plain-text body.
/// </summary>
/// <remarks>
/// Both bodies come from one localized view of the model, so they contain the same information in
/// the same grouping. Rendering is pure: no network, no SMTP, no checkpoint, no Jellyfin access.
/// </remarks>
public interface IDigestRenderer
{
    /// <summary>
    /// Renders a digest.
    /// </summary>
    /// <param name="model">The digest model.</param>
    /// <param name="recipient">Who it is for.</param>
    /// <returns>The rendered message.</returns>
    /// <exception cref="DigestRenderException">Rendering failed; nothing was produced.</exception>
    RenderedDigest Render(DigestModel model, DigestRecipient recipient);
}
