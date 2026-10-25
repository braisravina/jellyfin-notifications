using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.EmailDigest.Services;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <inheritdoc />
/// <remarks>
/// Builds one <see cref="DigestView"/> (localized and sanitized) and serializes it twice, so the
/// HTML and text bodies cannot disagree. A series lists at most <see cref="MaxEpisodesListed"/>
/// episodes, then "and N more"; that is wording only, since the model still holds every episode.
/// Any failure is wrapped in <see cref="DigestRenderException"/>.
/// </remarks>
public sealed class DigestRenderer : IDigestRenderer
{
    /// <summary>
    /// The default for the most episodes listed under one series; the digest model carries the
    /// configured value.
    /// </summary>
    public const int MaxEpisodesListed = 10;

    private readonly ILocalizationService _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="DigestRenderer"/> class.
    /// </summary>
    /// <param name="localization">The localization service.</param>
    public DigestRenderer(ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        _localization = localization;
    }

    /// <inheritdoc />
    public RenderedDigest Render(DigestModel model, DigestRecipient recipient)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(recipient);

        try
        {
            var view = BuildView(model, recipient);
            return new RenderedDigest(view.Subject, DigestHtmlWriter.Write(view), DigestTextWriter.Write(view));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DigestRenderException("The digest could not be rendered.", ex);
        }
    }

    private DigestView BuildView(DigestModel model, DigestRecipient recipient)
    {
        var text = new DigestText(_localization, recipient.Language);
        var name = DigestSanitizer.Clean(recipient.DisplayName);
        var greeting = name.Length > 0 ? text.Format("Greeting", name) : text.Format("GreetingNoName");

        if (model.IsEmpty)
        {
            return new DigestView(
                text.Language,
                text.Format("EmailSubject"),
                greeting,
                null,
                Array.Empty<DigestViewSection>(),
                text.Format("EmptyDigest"),
                text.Format("Footer"));
        }

        return new DigestView(
            text.Language,
            text.Plural("SubjectNew", model.Items.Count),
            greeting,
            text.Format("Intro"),
            model.Sections.Select(section => BuildSection(section, text, model.EpisodeListLimit)).ToList(),
            null,
            text.Format("Footer"));
    }

    private static DigestViewSection BuildSection(DigestSection section, DigestText text, int episodeLimit)
    {
        return new DigestViewSection(
            text.Heading(section.Category),
            section.Entries.Select(entry => BuildEntry(entry, text, episodeLimit)).ToList(),
            section.HiddenEntryCount > 0 ? text.Plural("Overflow", section.HiddenEntryCount) : null);
    }

    private static DigestViewEntry BuildEntry(DigestEntry entry, DigestText text, int episodeLimit)
    {
        var title = DigestSanitizer.Clean(entry.Title);

        switch (entry.Kind)
        {
            case DigestEntryKind.Movie:
                title = title.Length > 0 ? title : text.Format("Untitled");
                if (entry.Year is > 0)
                {
                    title += " (" + entry.Year.Value.ToString(CultureInfo.InvariantCulture) + ")";
                }

                return new DigestViewEntry(title, null, Array.Empty<string>(), null);

            case DigestEntryKind.Album:
                var artist = DigestSanitizer.Clean(entry.Artist);
                return new DigestViewEntry(
                    title.Length > 0 ? title : text.Format("Untitled"),
                    artist.Length > 0 ? text.Format("AlbumBy", artist) : null,
                    Array.Empty<string>(),
                    null);

            case DigestEntryKind.Series:
                var listed = entry.Episodes.Take(Math.Max(1, episodeLimit)).Select(episode => EpisodeLine(episode, text)).ToList();
                var hidden = entry.Episodes.Count - listed.Count;
                return new DigestViewEntry(
                    title.Length > 0 ? title : text.Format("UnknownSeries"),
                    text.Plural("EpisodeCount", entry.Episodes.Count),
                    listed,
                    hidden > 0 ? text.Plural("Overflow", hidden) : null);

            default:
                throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, "Unknown entry kind.");
        }
    }

    private static string EpisodeLine(DigestEpisode episode, DigestText text)
    {
        var title = DigestSanitizer.Clean(episode.Title);
        var season = episode.Season is >= 0 ? episode.Season : null;
        var number = episode.Number is >= 0 ? episode.Number : null;

        string? label = null;
        if (season is not null && number is not null)
        {
            label = text.Format("SeasonEpisode", Pad(season.Value), Pad(number.Value));
        }
        else if (season is not null)
        {
            label = text.Format("SeasonOnly", season.Value);
        }
        else if (number is not null)
        {
            label = text.Format("EpisodeOnly", number.Value);
        }

        if (label is null)
        {
            return title.Length > 0 ? title : text.Format("Untitled");
        }

        return title.Length > 0 ? label + " – " + title : label;
    }

    private static string Pad(int value)
    {
        return value.ToString("00", CultureInfo.InvariantCulture);
    }
}
