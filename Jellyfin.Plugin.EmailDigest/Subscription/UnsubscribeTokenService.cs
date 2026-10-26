using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.EmailDigest.Services;

namespace Jellyfin.Plugin.EmailDigest.Subscription;

/// <inheritdoc />
/// <remarks>
/// <para>
/// A token is <c>base64url(userId) "." base64url(HMAC-SHA256(key, "unsubscribe-v1" + userId))</c>. The
/// key is 256 random bits created on first use, kept in <c>unsubscribe.key</c> (owner-only) and never
/// leaving the server. A token therefore proves only "this server made a link for this user": it
/// carries no address, name or preference, cannot be forged without the key, and the only thing it can
/// do is turn that user's notifications off. It cannot read anything.
/// </para>
/// <para>
/// Tokens do not expire (an unsubscribe link in an old email must keep working) and are not single use
/// (unsubscribing twice is harmless). Deleting the key file invalidates every link ever sent, which is the
/// rotation procedure. A key of the wrong size is refused rather than replaced, since replacing it would
/// silently break every link already in people's inboxes.
/// </para>
/// </remarks>
public sealed class UnsubscribeTokenService : IUnsubscribeTokens
{
    private const string FileName = "unsubscribe.key";
    private const int KeyBytes = 32;
    private const string Purpose = "unsubscribe-v1";

    private readonly string _path;
    // One gate per key file, shared by every instance in the process, so two instances created at the
    // same moment cannot each generate a different key.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Gates = new(StringComparer.Ordinal);

    private readonly object _gate;
    private byte[]? _key;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsubscribeTokenService"/> class.
    /// </summary>
    /// <param name="folder">The folder holding plugin state.</param>
    public UnsubscribeTokenService(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _path = Path.Combine(folder, FileName);
        _gate = Gates.GetOrAdd(Path.GetFullPath(_path), _ => new object());
    }

    /// <inheritdoc />
    public string Create(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        return Encode(userId.ToByteArray()) + "." + Encode(Mac(userId));
    }

    /// <inheritdoc />
    public Guid? TryRead(string? token)
    {
        if (token is not { Length: > 0 and <= 128 })
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        var id = Decode(parts[0]);
        var presented = Decode(parts[1]);
        if (id is not { Length: 16 } || presented is not { Length: 32 })
        {
            return null;
        }

        var userId = new Guid(id);
        if (userId == Guid.Empty)
        {
            return null;
        }

        return CryptographicOperations.FixedTimeEquals(Mac(userId), presented) ? userId : null;
    }

    private static string Encode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[]? Decode(string value)
    {
        if (value.Length == 0 || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return null;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private byte[] Mac(Guid userId)
    {
        return HMACSHA256.HashData(Key(), Encoding.ASCII.GetBytes(Purpose).Concat(userId.ToByteArray()).ToArray());
    }

    private byte[] Key()
    {
        lock (_gate)
        {
            if (_key is not null)
            {
                return _key;
            }

            try
            {
                PrivateFile.EnsureDirectory(Path.GetDirectoryName(_path)!);
                if (!File.Exists(_path))
                {
                    var fresh = RandomNumberGenerator.GetBytes(KeyBytes);
                    var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var stream = PrivateFile.CreateOrReplace(temp))
                    {
                        stream.Write(fresh);
                    }

                    // No overwrite: if another writer won the race, its key is the one used.
                    if (!File.Exists(_path))
                    {
                        File.Move(temp, _path, overwrite: false);
                    }
                    else
                    {
                        File.Delete(temp);
                    }
                }

                var key = File.ReadAllBytes(_path);
                if (key.Length != KeyBytes)
                {
                    throw new UnsubscribeTokenException("The unsubscribe key has the wrong size and was left untouched.");
                }

                _key = key;
                return key;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new UnsubscribeTokenException("The unsubscribe key could not be read or created.", ex);
            }
        }
    }
}
