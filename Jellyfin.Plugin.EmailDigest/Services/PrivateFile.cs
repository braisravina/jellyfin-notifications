using System;
using System.IO;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Creates plugin state files and folders readable only by the account running the server.
/// </summary>
/// <remarks>
/// State now holds email addresses and verification token hashes, so it is not left to the process
/// umask. On Windows the mode is ignored and the folder's inherited permissions apply.
/// </remarks>
internal static class PrivateFile
{
    private const UnixFileMode FileMode600 = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode DirectoryMode700 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// Creates the folder (and any missing parents) if needed.
    /// </summary>
    /// <param name="path">The folder.</param>
    public static void EnsureDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, DirectoryMode700);
        }
    }

    /// <summary>
    /// Creates a new file for writing; it fails if the file already exists.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The open stream.</returns>
    public static FileStream CreateNew(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = FileMode600;
        }

        return new FileStream(path, options);
    }

    /// <summary>
    /// Creates a file for writing, replacing any leftover with the same name.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The open stream.</returns>
    public static FileStream CreateOrReplace(string path)
    {
        File.Delete(path);
        return CreateNew(path);
    }
}
