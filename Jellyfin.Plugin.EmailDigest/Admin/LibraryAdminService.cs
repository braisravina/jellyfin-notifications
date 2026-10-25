using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.EmailDigest.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// Lists Jellyfin's libraries for the administrator and saves which ones are monitored.
/// </summary>
/// <remarks>
/// The category stored for a library is exactly what the administrator picked: it is never derived
/// from a library's name or type. The only inference is <see cref="LibraryDto.SuggestedCategory"/>,
/// a starting value for the dropdown for the unambiguous Jellyfin types, which the administrator
/// confirms or changes (Jellyfin has no Anime type, so Anime is never suggested). Monitoring is an
/// explicit allowlist: a library added to Jellyfin later is not monitored until it is ticked here.
/// This class only chooses libraries; what each user may see is decided per user at digest time.
/// </remarks>
public sealed class LibraryAdminService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ISmtpConfigurationAccessor _configuration;
    private readonly ILogger<LibraryAdminService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryAdminService"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin's library manager.</param>
    /// <param name="configuration">Configuration access.</param>
    /// <param name="logger">The logger.</param>
    public LibraryAdminService(ILibraryManager libraryManager, ISmtpConfigurationAccessor configuration, ILogger<LibraryAdminService> logger)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        _libraryManager = libraryManager;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets the libraries and the current monitoring choices.
    /// </summary>
    /// <returns>200 with <see cref="LibrariesDto"/>. A Jellyfin failure propagates.</returns>
    public AdminResponse GetLibraries()
    {
        return new AdminResponse(200, Build(_configuration.GetCopy()));
    }

    /// <summary>
    /// Replaces the monitored libraries.
    /// </summary>
    /// <param name="update">The chosen libraries.</param>
    /// <returns>200 with the saved state, or 400 with the reasons nothing was saved.</returns>
    public AdminResponse UpdateLibraries(LibrariesUpdate? update)
    {
        if (update?.Monitored is null)
        {
            return new AdminResponse(400, new { error = "request" });
        }

        var existing = ExistingLibraryIds();
        var errors = new List<string>();
        var chosen = new List<MonitoredLibrary>();
        var seen = new HashSet<Guid>();

        for (var i = 0; i < update.Monitored.Count; i++)
        {
            var selection = update.Monitored[i];
            if (selection is null || selection.LibraryId == Guid.Empty)
            {
                errors.Add($"Monitored[{i}] has no valid library id.");
            }
            else if (!Enum.TryParse<MediaCategory>(selection.Category, ignoreCase: true, out var category) || !Enum.IsDefined(category))
            {
                errors.Add($"Monitored[{i}] needs a category: Movies, Series, Anime, Documentaries, Music or Other.");
            }
            else if (!seen.Add(selection.LibraryId))
            {
                errors.Add($"Monitored[{i}] lists a library that is already listed.");
            }
            else if (!existing.Contains(selection.LibraryId))
            {
                errors.Add($"Monitored[{i}] refers to a library that does not exist.");
            }
            else
            {
                chosen.Add(new MonitoredLibrary { LibraryId = selection.LibraryId, Category = category });
            }
        }

        if (errors.Count > 0)
        {
            return new AdminResponse(400, new { errors });
        }

        var configuration = _configuration.GetCopy();
        configuration.MonitoredLibraries.Clear();
        foreach (var library in chosen)
        {
            configuration.MonitoredLibraries.Add(library);
        }

        var configErrors = PluginConfigurationValidator.Validate(configuration);
        if (configErrors.Count > 0)
        {
            return new AdminResponse(400, new { errors = configErrors });
        }

        try
        {
            _configuration.Save(configuration);
        }
        catch (PluginConfigurationValidationException ex)
        {
            return new AdminResponse(400, new { errors = ex.Errors });
        }

        _logger.LogInformation("The monitored libraries were updated by an administrator ({Count} monitored).", chosen.Count);
        return new AdminResponse(200, Build(_configuration.GetCopy()));
    }

    private static string Suggest(string collectionType)
    {
        return collectionType.ToLowerInvariant() switch
        {
            "movies" => nameof(MediaCategory.Movies),
            "tvshows" => nameof(MediaCategory.Series),
            "music" => nameof(MediaCategory.Music),
            _ => string.Empty
        };
    }

    private HashSet<Guid> ExistingLibraryIds()
    {
        return _libraryManager.GetVirtualFolders()
            .Select(folder => Guid.TryParse(folder.ItemId, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
    }

    private LibrariesDto Build(PluginConfiguration configuration)
    {
        var configured = configuration.MonitoredLibraries
            .GroupBy(l => l.LibraryId)
            .ToDictionary(g => g.Key, g => g.First().Category);
        var libraries = new List<LibraryDto>();
        var present = new HashSet<Guid>();

        foreach (var folder in _libraryManager.GetVirtualFolders())
        {
            if (!Guid.TryParse(folder.ItemId, out var id) || id == Guid.Empty)
            {
                continue;
            }

            present.Add(id);
            var type = folder.CollectionType?.ToString() ?? string.Empty;
            var monitored = configured.TryGetValue(id, out var category);
            libraries.Add(new LibraryDto(id, folder.Name ?? string.Empty, type, Suggest(type), monitored, monitored ? category.ToString() : string.Empty));
        }

        var missing = configured
            .Where(pair => !present.Contains(pair.Key))
            .Select(pair => new MissingLibraryDto(pair.Key, pair.Value.ToString()))
            .ToList();

        return new LibrariesDto(
            libraries.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Id).ToList(),
            missing,
            Enum.GetNames<MediaCategory>());
    }
}
