using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <inheritdoc />
/// <remarks>
/// Two sources, in order of preference:
/// <list type="number">
/// <item>
/// <description>
/// A file named by the <c>EMAILDIGEST_SMTP_PASSWORD_FILE</c> environment variable, intended for a
/// Docker (or other orchestrator) secret. It is set by whoever deploys the container, not by a web
/// administrator, and cannot be changed from the web interface.
/// </description>
/// </item>
/// <item>
/// <description>
/// A file in the plugin's data folder, written with owner-only permissions on Unix. This is
/// <b>plain text</b>: permissions limit who can read the file, they do not encrypt it, and anyone
/// who can read the data folder or a backup of it can read the password. Jellyfin offers plugins no
/// encrypted secret store, and a key stored beside the data would add no real protection.
/// </description>
/// </item>
/// </list>
/// The password is never logged and never appears in exception messages.
/// </remarks>
public sealed class SmtpPasswordStore : ISmtpPasswordStore, IDisposable
{
    /// <summary>
    /// The environment variable naming a file that holds the password.
    /// </summary>
    public const string ExternalFileVariable = "EMAILDIGEST_SMTP_PASSWORD_FILE";

    private const int MaxLength = 512;
    private const string FileName = "smtp-password.secret";

    private readonly string _storedPath;
    private readonly Func<string?> _externalFile;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SmtpPasswordStore"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin's data folder.</param>
    public SmtpPasswordStore(string dataFolderPath)
        : this(dataFolderPath, () => Environment.GetEnvironmentVariable(ExternalFileVariable))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SmtpPasswordStore"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin's data folder.</param>
    /// <param name="externalFile">Returns the external password file path, or null/empty for none.</param>
    public SmtpPasswordStore(string dataFolderPath, Func<string?> externalFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
        ArgumentNullException.ThrowIfNull(externalFile);
        _storedPath = Path.Combine(dataFolderPath, FileName);
        _externalFile = externalFile;
    }

    /// <inheritdoc />
    public SmtpPasswordStatus GetStatus()
    {
        var external = _externalFile();
        if (!string.IsNullOrWhiteSpace(external))
        {
            return new SmtpPasswordStatus(ExternalHasContent(external), SmtpPasswordSource.External);
        }

        return File.Exists(_storedPath)
            ? new SmtpPasswordStatus(true, SmtpPasswordSource.Stored)
            : new SmtpPasswordStatus(false, SmtpPasswordSource.None);
    }

    /// <inheritdoc />
    public async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        var external = _externalFile();
        var path = !string.IsNullOrWhiteSpace(external) ? external : _storedPath;

        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        // Secret files commonly end with a newline; only trailing line breaks are removed.
        content = content.TrimEnd('\r', '\n');
        return content.Length == 0 ? null : content;
    }

    /// <inheritdoc />
    public async Task SetAsync(string password, CancellationToken cancellationToken)
    {
        ThrowIfExternal();

        if (string.IsNullOrEmpty(password) || password.Length > MaxLength || password.Any(char.IsControl))
        {
            throw new SmtpPasswordException(SmtpPasswordFailure.Invalid);
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PrivateFile.EnsureDirectory(Path.GetDirectoryName(_storedPath)!);
            var temp = _storedPath + ".tmp";
            File.Delete(temp);

            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None
            };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temp, options))
            {
                await stream.WriteAsync(new UTF8Encoding(false).GetBytes(password), cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, _storedPath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        ThrowIfExternal();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            File.Delete(_storedPath);
            File.Delete(_storedPath + ".tmp");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
    }

    private static bool ExternalHasContent(string path)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path, Encoding.UTF8).TrimEnd('\r', '\n').Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void ThrowIfExternal()
    {
        if (!string.IsNullOrWhiteSpace(_externalFile()))
        {
            throw new SmtpPasswordException(SmtpPasswordFailure.ManagedExternally);
        }
    }
}
