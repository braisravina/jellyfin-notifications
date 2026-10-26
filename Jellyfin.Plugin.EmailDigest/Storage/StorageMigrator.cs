using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Jellyfin.Plugin.EmailDigest.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.Storage;

/// <summary>
/// Copies state from the folder earlier versions used into the stable data folder.
/// </summary>
/// <remarks>
/// <para>
/// Earlier versions kept state in <c>plugins/Jellyfin.Plugin.EmailDigest/</c>, which is inside the
/// tree Jellyfin's plugin manager installs into and cleans. State now lives in
/// <c>&lt;server data directory&gt;/EmailDigest/</c>.
/// </para>
/// <para>
/// Rules. The old folder is only ever read: nothing in it is moved, changed or deleted, so it remains
/// the rollback copy for a downgrade. Each file is copied to a temporary name, verified by SHA-256
/// against the source, then renamed into place, so an interrupted copy never leaves a half-written
/// state file. A file that already exists at the destination is never overwritten (it may hold newer
/// state), and the choice is logged. The completion marker is written last, so an interrupted
/// migration simply runs again and finishes the files that are missing. Any failure stops the plugin
/// using state at all rather than starting from empty state.
/// </para>
/// </remarks>
public static class StorageMigrator
{
    /// <summary>
    /// The folder name under the server data directory.
    /// </summary>
    public const string FolderName = "EmailDigest";

    /// <summary>
    /// The completion marker file name.
    /// </summary>
    public const string MarkerName = "storage-migration.json";

    private const int MarkerFormat = 1;
    private const string TemporarySuffix = ".migrating";

    // Runtime-only files (the run lock, temporary files) are deliberately not migrated.
    private static readonly string[] StateFiles =
    [
        "user-preferences.json",
        "delivery-state.json",
        "delivery-state.json.bak",
        "checkpoint.json",
        "smtp-password.secret",
        "last-run.json"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Prepares the stable folder, migrating legacy state into it when needed.
    /// </summary>
    /// <param name="dataDirectory">The server's data directory (<c>IApplicationPaths.DataPath</c>).</param>
    /// <param name="legacyFolder">The folder earlier versions used.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger; messages never include file content or paths of private files.</param>
    /// <returns>The storage to use.</returns>
    /// <exception cref="StorageMigrationException">State could not be brought across; nothing was changed in the old folder.</exception>
    public static PluginStorage Prepare(string dataDirectory, string legacyFolder, TimeProvider time, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyFolder);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        var root = Path.Combine(dataDirectory, FolderName);

        try
        {
            PrivateFile.EnsureDirectory(root);

            var markerPath = Path.Combine(root, MarkerName);
            if (MarkerIsComplete(markerPath))
            {
                return new PluginStorage(root);
            }

            var sameFolder = string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyFolder)),
                StringComparison.Ordinal);

            var report = new List<MarkerEntry>();
            if (!sameFolder && Directory.Exists(legacyFolder))
            {
                foreach (var name in StateFiles)
                {
                    CopyOne(legacyFolder, root, name, report, logger);
                }
            }

            WriteMarker(markerPath, new Marker { Format = MarkerFormat, CompletedUtc = time.GetUtcNow(), Files = report });
            logger.LogInformation("Email Digest state is in the stable data folder ({Copied} file(s) copied from the previous location).", report.FindAll(e => e.Action == "copied").Count);
            return new PluginStorage(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError("Email Digest state could not be moved to the stable data folder ({ExceptionType}); the plugin will not run until this is fixed.", ex.GetType().Name);
            throw new StorageMigrationException("The plugin state could not be moved to the stable data folder. The previous folder was not changed.", ex);
        }
    }

    private static void CopyOne(string legacyFolder, string root, string name, List<MarkerEntry> report, ILogger logger)
    {
        var source = Path.Combine(legacyFolder, name);
        var destination = Path.Combine(root, name);
        var temporary = destination + TemporarySuffix;

        File.Delete(temporary);

        if (!File.Exists(source))
        {
            return;
        }

        var sourceHash = Hash(source);

        if (File.Exists(destination))
        {
            var same = string.Equals(Hash(destination), sourceHash, StringComparison.Ordinal);
            if (!same)
            {
                logger.LogWarning("A state file already exists in the stable folder and differs from the previous location; the stable folder's copy was kept.");
            }

            report.Add(new MarkerEntry { Name = name, Action = same ? "identical" : "kept-destination", Sha256 = sourceHash });
            return;
        }

        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = PrivateFile.CreateNew(temporary))
        {
            input.CopyTo(output);
        }

        if (!string.Equals(Hash(temporary), sourceHash, StringComparison.Ordinal) || !string.Equals(Hash(source), sourceHash, StringComparison.Ordinal))
        {
            File.Delete(temporary);
            throw new IOException("A copied state file did not match its source.");
        }

        File.Move(temporary, destination, overwrite: false);
        report.Add(new MarkerEntry { Name = name, Action = "copied", Sha256 = sourceHash });
    }

    private static bool MarkerIsComplete(string markerPath)
    {
        try
        {
            if (!File.Exists(markerPath))
            {
                return false;
            }

            var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(markerPath), JsonOptions);
            return marker is { Format: MarkerFormat };
        }
        catch (JsonException)
        {
            // A damaged marker only means "run the idempotent migration again".
            return false;
        }
    }

    private static void WriteMarker(string markerPath, Marker marker)
    {
        var temporary = markerPath + TemporarySuffix;
        using (var stream = PrivateFile.CreateOrReplace(temporary))
        {
            JsonSerializer.Serialize(stream, marker, JsonOptions);
        }

        File.Move(temporary, markerPath, overwrite: true);
    }

    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class Marker
    {
        public int Format { get; set; }

        public DateTimeOffset CompletedUtc { get; set; }

        public List<MarkerEntry> Files { get; set; } = new();
    }

    private sealed class MarkerEntry
    {
        public string Name { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string Sha256 { get; set; } = string.Empty;
    }
}
