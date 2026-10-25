using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Models;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <inheritdoc />
/// <remarks>
/// Section order is fixed (Movies, Series, Anime, Documentaries, Music, Other). Entries are ordered
/// by title (ordinal, case-insensitive) then by id; episodes by season, number, title and id, with
/// unknown values last. Episodes group under their series by series id, else by series name, else
/// into one "unknown series" entry per category. Entries are never cut inside a series: a limit
/// hides whole entries and reports them in the hidden counts. The per-digest limit is shared fairly
/// between the categories (see Allocate), not spent in section order.
/// </remarks>
public sealed class DigestBuilder : IDigestBuilder
{
    private static readonly MediaCategory[] SectionOrder =
    {
        MediaCategory.Movies,
        MediaCategory.Series,
        MediaCategory.Anime,
        MediaCategory.Documentaries,
        MediaCategory.Music,
        MediaCategory.Other
    };

    /// <inheritdoc />
    public DigestModel Build(IReadOnlyCollection<DiscoveredMediaItem> items, DigestOptions options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var unique = items
            .Where(item => item is not null)
            .GroupBy(item => item.ItemId)
            .Select(group => group
                .OrderBy(item => item.LibraryId)
                .ThenBy(item => (int)item.Category)
                .First())
            .OrderBy(item => item.ItemId)
            .ToList();

        var entriesByCategory = new List<(MediaCategory Category, List<DigestEntry> Entries)>();
        foreach (var category in SectionOrder)
        {
            var entries = BuildEntries(unique.Where(item => item.Category == category).ToList());
            if (entries.Count > 0)
            {
                entriesByCategory.Add((category, entries));
            }
        }

        var allocation = Allocate(
            entriesByCategory.Select(c => Math.Min(c.Entries.Count, options.MaxEntriesPerSection)).ToList(),
            options.MaxEntriesPerDigest);

        var sections = new List<DigestSection>();
        for (var i = 0; i < entriesByCategory.Count; i++)
        {
            var (category, allEntries) = entriesByCategory[i];
            var shown = allEntries.Take(allocation[i]).ToList();
            var hidden = allEntries.Skip(allocation[i]).ToList();

            sections.Add(new DigestSection(
                category,
                shown,
                allEntries.Count,
                allEntries.Sum(entry => entry.ItemIds.Count),
                hidden.Count,
                hidden.Sum(entry => entry.ItemIds.Count)));
        }

        // Categories outside SectionOrder cannot occur today; if one is added to the enum without
        // being listed above, fail loudly rather than silently dropping its items.
        var covered = sections.Sum(section => section.TotalItemCount);
        if (covered != unique.Count)
        {
            throw new InvalidOperationException("Some discovered items have a category that has no digest section.");
        }

        return new DigestModel(sections, unique, options.MaxEpisodesPerSeries);
    }

    // Shares the digest-wide budget fairly: every section gets an equal share, a section that needs
    // less than its share frees the rest for the others, and leftover single entries go to the
    // sections in display order. A large category therefore cannot push all others out of view.
    private static int[] Allocate(IReadOnlyList<int> caps, int budget)
    {
        var allocation = new int[caps.Count];
        var remaining = budget;

        while (remaining > 0)
        {
            var active = Enumerable.Range(0, caps.Count).Where(i => allocation[i] < caps[i]).ToList();
            if (active.Count == 0)
            {
                break;
            }

            var share = remaining / active.Count;
            if (share == 0)
            {
                foreach (var i in active.Take(remaining))
                {
                    allocation[i]++;
                }

                break;
            }

            foreach (var i in active)
            {
                var give = Math.Min(share, caps[i] - allocation[i]);
                allocation[i] += give;
                remaining -= give;
            }
        }

        return allocation;
    }

    private static List<DigestEntry> BuildEntries(IReadOnlyList<DiscoveredMediaItem> categoryItems)
    {
        var entries = new List<DigestEntry>();

        foreach (var item in categoryItems.Where(i => i.ItemType != DiscoveredMediaItemType.Episode))
        {
            entries.Add(new DigestEntry(
                item.ItemType == DiscoveredMediaItemType.MusicAlbum ? DigestEntryKind.Album : DigestEntryKind.Movie,
                item.Name ?? string.Empty,
                item.ProductionYear,
                string.IsNullOrWhiteSpace(item.AlbumArtist) ? null : item.AlbumArtist,
                Array.Empty<DigestEpisode>(),
                new[] { item.ItemId }));
        }

        var episodeGroups = categoryItems
            .Where(i => i.ItemType == DiscoveredMediaItemType.Episode)
            .GroupBy(SeriesKey);

        foreach (var group in episodeGroups)
        {
            var episodes = group
                .OrderBy(e => e.SeasonNumber ?? int.MaxValue)
                .ThenBy(e => e.EpisodeNumber ?? int.MaxValue)
                .ThenBy(e => e.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.ItemId)
                .ToList();

            var seriesName = episodes
                .Select(e => e.SeriesName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? string.Empty;

            entries.Add(new DigestEntry(
                DigestEntryKind.Series,
                seriesName,
                null,
                null,
                episodes.Select(e => new DigestEpisode(e.SeasonNumber, e.EpisodeNumber, e.Name ?? string.Empty)).ToList(),
                episodes.Select(e => e.ItemId).ToList()));
        }

        return entries
            .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => (int)entry.Kind)
            .ThenBy(entry => entry.ItemIds[0])
            .ToList();
    }

    private static string SeriesKey(DiscoveredMediaItem episode)
    {
        if (episode.SeriesId is { } seriesId && seriesId != Guid.Empty)
        {
            return "id:" + seriesId.ToString("N");
        }

        return string.IsNullOrWhiteSpace(episode.SeriesName)
            ? "unknown"
            : "name:" + episode.SeriesName.Trim().ToUpperInvariant();
    }
}
