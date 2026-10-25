namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// One new episode inside a series entry. Missing values stay <see langword="null"/> or empty;
/// the renderer decides how to word them.
/// </summary>
/// <param name="Season">The season number, when known.</param>
/// <param name="Number">The episode number, when known.</param>
/// <param name="Title">The episode title; empty when unknown.</param>
public sealed record DigestEpisode(int? Season, int? Number, string Title);
