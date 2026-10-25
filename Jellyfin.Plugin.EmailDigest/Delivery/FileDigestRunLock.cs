using System;
using System.IO;
using System.Threading;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// An exclusive lock held for the length of a digest run, by this process and, through an
/// operating-system file lock, by any other process using the same data folder.
/// </summary>
/// <remarks>
/// The lock is the open file handle, not the file's existence: the operating system releases it when
/// the handle is closed or the process dies, so a crash cannot leave a permanent lock. A lock file
/// left behind on disk is harmless and is simply reopened. Scheduled and manual runs both go through
/// this class, so they cannot overlap.
/// </remarks>
public sealed class FileDigestRunLock : IDigestRunLock
{
    private const string FileName = "digest-run.lock";

    // One run at a time inside this process, whatever the file system does.
    private static readonly SemaphoreSlim ProcessLock = new(1, 1);

    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileDigestRunLock"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin's data folder.</param>
    public FileDigestRunLock(string dataFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
        _path = Path.Combine(dataFolderPath, FileName);
    }

    /// <inheritdoc />
    public IDisposable? TryAcquire()
    {
        if (!ProcessLock.Wait(0))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var stream = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new Handle(stream);
        }
        catch (IOException)
        {
            // Another process holds the file lock.
            ProcessLock.Release();
            return null;
        }
        catch
        {
            ProcessLock.Release();
            throw;
        }
    }

    private sealed class Handle : IDisposable
    {
        private FileStream? _stream;

        public Handle(FileStream stream)
        {
            _stream = stream;
        }

        public void Dispose()
        {
            var stream = Interlocked.Exchange(ref _stream, null);
            if (stream is null)
            {
                return;
            }

            stream.Dispose();
            ProcessLock.Release();
        }
    }
}
