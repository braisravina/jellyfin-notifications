using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// The libraries Jellyfin has, the monitoring choices made, and configured libraries that no longer exist.
/// </summary>
/// <param name="Libraries">Jellyfin's libraries.</param>
/// <param name="Missing">Configured libraries Jellyfin no longer has; saving drops them.</param>
/// <param name="Categories">The category names a library can be mapped to.</param>
public sealed record LibrariesDto(IReadOnlyList<LibraryDto> Libraries, IReadOnlyList<MissingLibraryDto> Missing, IReadOnlyList<string> Categories);
