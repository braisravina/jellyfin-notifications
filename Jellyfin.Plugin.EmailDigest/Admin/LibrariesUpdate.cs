using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// The complete list of monitored libraries; it replaces the current list. An empty list means
/// "monitor nothing", never "monitor everything".
/// </summary>
/// <param name="Monitored">The chosen libraries.</param>
public sealed record LibrariesUpdate(IReadOnlyList<LibrarySelection>? Monitored);
