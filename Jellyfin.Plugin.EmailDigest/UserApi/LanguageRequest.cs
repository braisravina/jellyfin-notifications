namespace Jellyfin.Plugin.EmailDigest.UserApi;

/// <summary>
/// A request to change the language of the signed-in user's digests and emails.
/// </summary>
/// <param name="Language">en, es or gl.</param>
public sealed record LanguageRequest(string? Language);
