using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.Verification;

/// <inheritdoc />
/// <remarks>
/// <para>
/// Tokens are 256 random bits from the operating system's generator, shown once (in the email) and
/// stored only as a SHA-256 hash. Because the token is already unguessable, a plain hash is the right
/// tool: the stored hash cannot be turned back into a link, and there is nothing to brute-force.
/// </para>
/// <para>
/// One outstanding request per user: a new one replaces the old, whose link stops working. A link
/// is single use and expires. Unknown, used, expired, replaced and cancelled tokens all look the same
/// to the caller. Requests are rate limited per user, per address and overall, from a log that is
/// persisted so a restart does not reset it. Every read-modify-write runs under one lock.
/// </para>
/// <para>
/// The state file holds only hashes and pending addresses. If it is damaged it is moved aside (never
/// deleted) and verification starts empty: nothing sensitive is lost, pending links simply stop working
/// and users ask again.
/// </para>
/// </remarks>
public sealed class AddressVerificationService : IAddressVerificationService, IDisposable
{
    private const string FileName = "verification-requests.json";
    private const int CurrentFormat = 1;
    private const int TokenBytes = 32;
    private const int TokenLength = 43; // 32 bytes as unpadded base64url
    private static readonly TimeSpan LogRetention = TimeSpan.FromHours(24);

    private readonly string _path;
    private readonly Func<VerificationPolicy> _policy;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="AddressVerificationService"/> class.
    /// </summary>
    /// <param name="folder">The folder holding plugin state.</param>
    /// <param name="policy">Supplies the current policy, so a changed lifetime applies to the next request.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger; it is never given a token, a hash or an address.</param>
    public AddressVerificationService(string folder, Func<VerificationPolicy> policy, TimeProvider time, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _path = Path.Combine(folder, FileName);
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Normalizes an address for verification.
    /// </summary>
    /// <param name="address">The submitted address.</param>
    /// <returns>The trimmed address, or null when it is not a valid single-line address.</returns>
    public static string? Normalize(string? address)
    {
        var trimmed = address?.Trim();
        return !string.IsNullOrEmpty(trimmed)
               && ConfigurationValidation.IsValidEmail(trimmed)
               && ConfigurationValidation.IsValidSingleLine(trimmed, 254)
               && MimeKit.MailboxAddress.TryParse(trimmed, out var parsed)
               && string.Equals(parsed.Address, trimmed, StringComparison.Ordinal)
            ? trimmed
            : null;
    }

    /// <inheritdoc />
    public async Task<IssueResult> IssueAsync(Guid userId, string? address, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        var normalized = Normalize(address);
        if (normalized is null)
        {
            return IssueResult.Invalid();
        }

        var policy = _policy();
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            var state = await LoadAsync(now, cancellationToken).ConfigureAwait(false);

            var limited = CheckLimits(state, userId, AddressKey(normalized), now, policy);
            if (limited is not null)
            {
                return limited;
            }

            var token = NewToken();
            state.Pending.RemoveAll(r => r.UserId == userId);
            state.Pending.Add(new VerificationRequest
            {
                UserId = userId,
                Address = normalized,
                TokenHash = HashToken(token),
                CreatedUtc = now,
                ExpiresUtc = now + policy.Lifetime
            });
            state.Log.Add(new LogEntry { UserId = userId, AddressKey = AddressKey(normalized), AtUtc = now });

            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return IssueResult.Ok(token, normalized, now + policy.Lifetime);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ConfirmOutcome> ConfirmAsync(string? token, Func<ConfirmedAddress, CancellationToken, Task<bool>> apply, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);

        if (!LooksLikeToken(token))
        {
            return ConfirmOutcome.Invalid;
        }

        var presented = Encoding.ASCII.GetBytes(HashToken(token!));
        ConfirmedAddress confirmed;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            var state = await LoadAsync(now, cancellationToken).ConfigureAwait(false);

            // Every stored hash is compared, in constant time per comparison, so timing does not
            // reveal how much of a guess matched.
            VerificationRequest? match = null;
            foreach (var request in state.Pending)
            {
                if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(request.TokenHash), presented))
                {
                    match = request;
                }
            }

            if (match is null)
            {
                return ConfirmOutcome.Invalid;
            }

            // Used up first, and persisted: a failure after this point cannot make the link reusable.
            state.Pending.Remove(match);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            confirmed = new ConfirmedAddress(match.UserId, match.Address);
        }
        finally
        {
            _lock.Release();
        }

        var applied = await apply(confirmed, cancellationToken).ConfigureAwait(false);
        return applied ? ConfirmOutcome.Confirmed : ConfirmOutcome.NotApplied;
    }

    /// <inheritdoc />
    public async Task<PendingVerification?> GetPendingAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(_time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            var request = state.Pending.FirstOrDefault(r => r.UserId == userId);
            return request is null ? null : new PendingVerification(request.Address, request.CreatedUtc, request.ExpiresUtc);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> CancelAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(_time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            if (state.Pending.RemoveAll(r => r.UserId == userId) == 0)
            {
                return false;
            }

            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return true;
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

    private static IssueResult? CheckLimits(State state, Guid userId, string addressKey, DateTimeOffset now, VerificationPolicy policy)
    {
        var mine = state.Log.Where(e => e.UserId == userId).Select(e => e.AtUtc).OrderBy(t => t).ToList();

        if (mine.Count > 0 && now - mine[^1] < policy.Cooldown)
        {
            return IssueResult.Limited(RateLimitKind.Cooldown, mine[^1] + policy.Cooldown - now);
        }

        return Within(mine, TimeSpan.FromHours(1), policy.PerUserPerHour, RateLimitKind.User, now)
               ?? Within(mine, TimeSpan.FromHours(24), policy.PerUserPerDay, RateLimitKind.User, now)
               ?? Within(state.Log.Where(e => e.AddressKey == addressKey).Select(e => e.AtUtc).OrderBy(t => t).ToList(), TimeSpan.FromHours(24), policy.PerAddressPerDay, RateLimitKind.Address, now)
               ?? Within(state.Log.Select(e => e.AtUtc).OrderBy(t => t).ToList(), TimeSpan.FromHours(1), policy.GlobalPerHour, RateLimitKind.Global, now);
    }

    private static IssueResult? Within(List<DateTimeOffset> times, TimeSpan window, int limit, RateLimitKind kind, DateTimeOffset now)
    {
        var inWindow = times.Where(t => now - t < window).ToList();
        return inWindow.Count >= limit
            ? IssueResult.Limited(kind, inWindow[inWindow.Count - limit] + window - now)
            : null;
    }

    private static bool LooksLikeToken(string? token)
    {
        return token is { Length: TokenLength } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

    private static string NewToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
    }

    // Rate limits are per address, not per spelling; the log keeps a hash, not the address.
    private static string AddressKey(string address)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address.ToLowerInvariant())));
    }

    private async Task<State> LoadAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        State? state = null;
        try
        {
            var file = await AtomicJsonFile.ReadAsync<StateFile>(_path, cancellationToken).ConfigureAwait(false);
            if (file is not null)
            {
                if (file.Format == CurrentFormat && file.Pending is not null && file.Log is not null)
                {
                    state = new State { Pending = file.Pending, Log = file.Log };
                }
                else
                {
                    Quarantine();
                }
            }
        }
        catch (JsonException)
        {
            Quarantine();
        }
        catch (IOException ex)
        {
            throw new VerificationStoreException("Verification state could not be read.", ex);
        }

        state ??= new State();
        state.Pending.RemoveAll(r => r.ExpiresUtc <= now || string.IsNullOrEmpty(r.TokenHash) || r.UserId == Guid.Empty);
        state.Log.RemoveAll(e => now - e.AtUtc >= LogRetention);
        return state;
    }

    // Moved aside rather than deleted: it holds nothing that cannot be recreated, but it may help diagnosis.
    private void Quarantine()
    {
        try
        {
            var aside = _path + ".damaged-" + _time.GetUtcNow().ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
            File.Move(_path, aside, overwrite: true);
            _logger.LogWarning("The verification state file was unreadable and was moved aside; pending verification links no longer work.");
        }
        catch (IOException ex)
        {
            throw new VerificationStoreException("Damaged verification state could not be moved aside.", ex);
        }
    }

    private async Task SaveAsync(State state, CancellationToken cancellationToken)
    {
        try
        {
            await AtomicJsonFile.WriteAsync(_path, new StateFile { Format = CurrentFormat, Pending = state.Pending, Log = state.Log }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VerificationStoreException("Verification state could not be saved.", ex);
        }
    }

    private sealed class State
    {
        public List<VerificationRequest> Pending { get; set; } = new();

        public List<LogEntry> Log { get; set; } = new();
    }

    private sealed class StateFile
    {
        public int Format { get; set; }

        public List<VerificationRequest>? Pending { get; set; }

        public List<LogEntry>? Log { get; set; }
    }

    private sealed class LogEntry
    {
        public Guid UserId { get; set; }

        public string AddressKey { get; set; } = string.Empty;

        public DateTimeOffset AtUtc { get; set; }
    }
}
