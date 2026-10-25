using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// One displayed line of a digest section: a movie, an album, or a series with its new episodes.
/// </summary>
/// <param name="Kind">What the entry is.</param>
/// <param name="Title">The movie, album or series title; empty when Jellyfin has none.</param>
/// <param name="Year">The production year of a movie or album, when known.</param>
/// <param name="Artist">The album artist of an album, when known.</param>
/// <param name="Episodes">The new episodes of a series entry, ordered; empty for other kinds.</param>
/// <param name="ItemIds">The Jellyfin ids of every item this entry stands for, ordered.</param>
public sealed record DigestEntry(
    DigestEntryKind Kind,
    string Title,
    int? Year,
    string? Artist,
    IReadOnlyList<DigestEpisode> Episodes,
    IReadOnlyList<Guid> ItemIds);
