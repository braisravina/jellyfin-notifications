using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.EmailDigest.UserApi;

/// <summary>
/// Limits how often one signed-in user can make changes, so a script or a stuck page cannot hammer the
/// preference files or the mail server.
/// </summary>
/// <remarks>
/// A sliding window per user, in memory (a restart forgets it, which is harmless: it is a courtesy
/// limit, not a security boundary). The limits that matter for abuse, how often an address can be asked to
/// be verified, are enforced and persisted by the verification service.
/// </remarks>
public sealed class UserRequestThrottle
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Queue<DateTimeOffset>> _calls = new();
    private readonly TimeProvider _time;
    private readonly int _limit;
    private readonly TimeSpan _window;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserRequestThrottle"/> class.
    /// </summary>
    /// <param name="time">The clock.</param>
    /// <param name="limit">How many changes a user may make in the window.</param>
    /// <param name="window">The window.</param>
    public UserRequestThrottle(TimeProvider time, int limit = 30, TimeSpan? window = null)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _limit = limit;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Counts a change by a user and says whether it is allowed.
    /// </summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="retryAfter">When refused, how long until another is allowed.</param>
    /// <returns>True when the change may proceed.</returns>
    public bool TryAcquire(Guid userId, out TimeSpan retryAfter)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_calls.Count > 1024)
            {
                foreach (var stale in _calls.Where(c => c.Value.Count == 0 || now - c.Value.Peek() >= _window).Select(c => c.Key).ToList())
                {
                    _calls.Remove(stale);
                }
            }

            if (!_calls.TryGetValue(userId, out var queue))
            {
                queue = new Queue<DateTimeOffset>();
                _calls[userId] = queue;
            }

            while (queue.Count > 0 && now - queue.Peek() >= _window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= _limit)
            {
                retryAfter = queue.Peek() + _window - now;
                return false;
            }

            queue.Enqueue(now);
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }
}
