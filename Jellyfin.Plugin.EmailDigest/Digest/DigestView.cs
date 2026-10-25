using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// Everything a message says, already localized and sanitized, independent of output format.
/// </summary>
/// <param name="Language">The language used, after fallback.</param>
/// <param name="Subject">The single-line subject.</param>
/// <param name="Greeting">The greeting line.</param>
/// <param name="Intro">The introduction, or null for an empty digest.</param>
/// <param name="Sections">The sections, empty for an empty digest.</param>
/// <param name="EmptyMessage">The "nothing new" message, or null when there is content.</param>
/// <param name="ContinuationNote">A note that hidden items follow in the next digest, or null.</param>
/// <param name="Footer">The footer line.</param>
internal sealed record DigestView(
    string Language,
    string Subject,
    string Greeting,
    string? Intro,
    IReadOnlyList<DigestViewSection> Sections,
    string? EmptyMessage,
    string? ContinuationNote,
    string Footer);
