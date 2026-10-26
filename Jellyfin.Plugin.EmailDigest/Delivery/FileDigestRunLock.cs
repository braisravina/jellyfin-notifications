using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Jellyfin.Plugin.EmailDigest.Services;

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

    // One run at a time per data folder inside this process, whatever the file system does.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessLocks = new(StringComparer.Ordinal);

    private readonly string _path;
    private readonly SemaphoreSlim _processLock;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileDigestRunLock"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin's data folder.</param>
    public FileDigestRunLock(string dataFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
        _path = Path.GetFullPath(Path.Combine(dataFolderPath, FileName));
        _processLock = ProcessLocks.GetOrAdd(_path, _ => new SemaphoreSlim(1, 1));
    }

    /// <inheritdoc />
    public IDisposable? TryAcquire()
    {
        if (!_processLock.Wait(0))
        {
            return null;
        }

        try
        {
            PrivateFile.EnsureDirectory(Path.GetDirectoryName(_path)!);
            var stream = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new Handle(stream, _processLock);
        }
        catch (IOException)
        {
            // Another process holds the file lock.
            _processLock.Release();
            return null;
        }
        catch
        {
            _processLock.Release();
            throw;
        }
    }

    private sealed class Handle : IDisposable
    {
        private readonly SemaphoreSlim _processLock;
        private FileStream? _stream;

        public Handle(FileStream stream, SemaphoreSlim processLock)
        {
            _stream = stream;
            _processLock = processLock;
        }

        public void Dispose()
        {
            var stream = Interlocked.Exchange(ref _stream, null);
            if (stream is null)
            {
                return;
            }

            stream.Dispose();
            _processLock.Release();
        }
    }
}
