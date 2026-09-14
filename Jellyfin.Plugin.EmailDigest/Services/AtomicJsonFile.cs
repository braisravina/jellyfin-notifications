using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Reads and writes a single JSON file with a write-temp-then-rename pattern, so a reader never
/// observes a partially-written file and a crash mid-write leaves the previous version intact.
/// </summary>
/// <remarks>
/// Jellyfin's own plugin configuration persistence (<c>MyXmlSerializer.SerializeToFile</c>) writes
/// directly to the target file with no such protection; this plugin's own state files
/// (checkpoint, per-user preferences) deliberately do better since losing or corrupting them has
/// real consequences (duplicate or skipped notifications). The rename step is atomic on Linux
/// (the platform this plugin is developed and deployed against); on Windows it is best-effort, as
/// .NET's cross-platform <see cref="File.Move(string, string, bool)"/> is the only portable
/// primitive available without native interop.
/// </remarks>
internal static class AtomicJsonFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Reads and deserializes the JSON file at <paramref name="path"/>.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The deserialized value, or the default value if the file does not exist.</returns>
    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes <paramref name="value"/> and atomically replaces the file at <paramref name="path"/>.
    /// </summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="path">The file path.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";

        using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
