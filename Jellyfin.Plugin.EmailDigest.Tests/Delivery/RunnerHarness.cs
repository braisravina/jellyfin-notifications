using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Admin;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Digest;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.EmailDigest.Tests.Delivery;

/// <summary>
/// Wires a <see cref="DigestRunner"/> to real storage in a temporary folder and fakes for everything
/// that would touch Jellyfin or the network. No real mail can be sent from here.
/// </summary>
internal sealed class RunnerHarness : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "emaildigest-run-" + Guid.NewGuid().ToString("N"));

    public RunnerHarness()
    {
        Directory.CreateDirectory(_root);
        Time = new ManualTime(Start);
        Config = new PluginConfiguration
        {
            DigestEnabled = true,
            DigestDryRun = false,
            SmtpEnabled = true,
            SmtpHost = "smtp.example.com",
            SmtpPort = 587,
            SmtpSecurity = SmtpSecurityMode.StartTls,
            SenderEmailAddress = "digest@example.com",
            DefaultLanguage = "en",
            FallbackLookbackHours = 168
        };
        Config.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = Library, Category = MediaCategory.Movies });

        Preferences = new UserPreferenceRepository(_root);
        Passwords = new SmtpPasswordStore(_root, () => null);
        InnerState = new DeliveryStateStore(_root, Time);
        State = InnerState;
        Summaries = new RunSummaryStore(_root);
        Lock = new FileDigestRunLock(_root);
        Validator.Setup(v => v.Validate(It.IsAny<IReadOnlyList<MonitoredLibrary>>())).Returns(Array.Empty<MonitoredLibraryIssue>());
    }

    public static Guid Library { get; } = Guid.NewGuid();

    public string Root => _root;

    public ManualTime Time { get; }

    public PluginConfiguration Config { get; }

    public UserPreferenceRepository Preferences { get; }

    public SmtpPasswordStore Passwords { get; }

    public DeliveryStateStore InnerState { get; }

    public IDeliveryStateStore State { get; set; }

    public RunSummaryStore Summaries { get; }

    public IDigestRunLock Lock { get; set; }

    public Mock<IMonitoredLibraryValidator> Validator { get; } = new();

    public FakeUsers Users { get; } = new();

    public FakeDiscovery Discovery { get; } = new();

    public FakeTransport Transport { get; } = new();

    public IDigestRenderer Renderer { get; set; } = new DigestRenderer(new LocalizationService());

    public DigestRetryPolicy Retry { get; set; } = new(3, new[] { TimeSpan.Zero, TimeSpan.Zero });

    public CapturingLogger<DigestRunner> Log { get; } = new();

    public Task<DigestRunSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        return NewRunner().RunAsync(cancellationToken);
    }

    public DigestRunner NewRunner()
    {
        return new DigestRunner(
            new FixedConfig(Config),
            Validator.Object,
            Preferences,
            Users,
            Discovery,
            new DigestBuilder(),
            Renderer,
            Transport,
            Passwords,
            State,
            Summaries,
            Lock,
            Retry,
            Time,
            Log);
    }

    /// <summary>When the harness's users' addresses count as having been verified.</summary>
    public static readonly DateTimeOffset Verified = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<Guid> AddUserAsync(string name, string email, string language = "en", bool notifications = true, bool disabledAccount = false)
    {
        var id = Guid.NewGuid();
        await Preferences.SaveAsync(new UserNotificationPreferences { EmailVerifiedUtc = Verified, UserId = id, Email = email, Language = language, NotificationsEnabled = notifications }, CancellationToken.None);
        Users.Add(id, new UserInfo(name, disabledAccount));
        return id;
    }

    public static DiscoveredMediaItem Movie(string name, DateTimeOffset added, MediaCategory category = MediaCategory.Movies, Guid? id = null)
    {
        return new DiscoveredMediaItem
        {
            ItemId = id ?? Guid.NewGuid(),
            ItemType = DiscoveredMediaItemType.Movie,
            Name = name,
            ProductionYear = 2020,
            DateAddedUtc = added,
            Category = category,
            LibraryId = Library
        };
    }

    public void Dispose()
    {
        Preferences.Dispose();
        Passwords.Dispose();
        InnerState.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    internal sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTime(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
        }
    }

    internal sealed class FixedConfig : ISmtpConfigurationAccessor
    {
        private readonly PluginConfiguration _config;

        public FixedConfig(PluginConfiguration config)
        {
            _config = config;
        }

        public PluginConfiguration GetCopy()
        {
            return _config;
        }

        public void Save(PluginConfiguration configuration)
        {
            throw new InvalidOperationException("A digest run must never change the configuration.");
        }
    }

    internal sealed class FakeUsers : IUserDirectory
    {
        private readonly Dictionary<Guid, UserInfo> _users = new();

        public void Add(Guid id, UserInfo info)
        {
            _users[id] = info;
        }

        public UserInfo? Find(Guid userId)
        {
            return _users.TryGetValue(userId, out var info) ? info : null;
        }
    }

    /// <summary>
    /// Stands in for Jellyfin's user-scoped discovery: each user is given exactly the items they may
    /// see, and the time window is applied the way the real service applies it.
    /// </summary>
    internal sealed class FakeDiscovery : IMediaDiscoveryService
    {
        private readonly Dictionary<Guid, List<DiscoveredMediaItem>> _items = new();
        private readonly HashSet<Guid> _failing = new();

        public ConcurrentQueue<(Guid User, DateTimeOffset From, DateTimeOffset To)> Calls { get; } = new();

        public void Give(Guid user, params DiscoveredMediaItem[] items)
        {
            if (!_items.TryGetValue(user, out var list))
            {
                _items[user] = list = new List<DiscoveredMediaItem>();
            }

            list.AddRange(items);
        }

        public void Replace(Guid user, params DiscoveredMediaItem[] items)
        {
            _items[user] = items.ToList();
        }

        public void FailFor(Guid user)
        {
            _failing.Add(user);
        }

        public Task<IReadOnlyList<DiscoveredMediaItem>> DiscoverAsync(Guid userId, IReadOnlyList<MonitoredLibrary> monitoredLibraries, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Enqueue((userId, fromUtc, toUtc));
            if (_failing.Contains(userId))
            {
                throw new MediaDiscoveryException("library scan failed", new InvalidOperationException("boom"));
            }

            IReadOnlyList<DiscoveredMediaItem> result = _items.TryGetValue(userId, out var list)
                ? list.Where(i => i.DateAddedUtc > fromUtc && i.DateAddedUtc <= toUtc).ToList()
                : new List<DiscoveredMediaItem>();
            return Task.FromResult(result);
        }
    }

    internal sealed class FakeTransport : IEmailTransport
    {
        private readonly object _gate = new();

        public List<(SmtpSettings Settings, OutgoingEmail Email)> Sent { get; } = new();

        public Func<OutgoingEmail, int, EmailTransportResult> Behavior { get; set; } = (_, _) => new EmailTransportResult(EmailTransportOutcome.Accepted);

        public Func<OutgoingEmail, CancellationToken, Task>? BeforeAnswer { get; set; }

        public int Attempts(string recipient)
        {
            lock (_gate)
            {
                return Sent.Count(s => s.Email.RecipientAddress == recipient);
            }
        }

        public Task<EmailTransportResult> TestConnectionAsync(SmtpSettings settings, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("A digest run must not test the connection.");
        }

        public async Task<EmailTransportResult> SendAsync(SmtpSettings settings, OutgoingEmail email, CancellationToken cancellationToken)
        {
            int attempt;
            lock (_gate)
            {
                Sent.Add((settings, email));
                attempt = Sent.Count(s => s.Email.RecipientAddress == email.RecipientAddress);
            }

            if (BeforeAnswer is not null)
            {
                await BeforeAnswer(email, cancellationToken);
            }

            return Behavior(email, attempt);
        }
    }

    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Enqueue(formatter(state, exception));
        }
    }

    /// <summary>
    /// Decorates the real state store so a write can be made to fail.
    /// </summary>
    internal sealed class FailingState : IDeliveryStateStore
    {
        private readonly IDeliveryStateStore _inner;

        public FailingState(IDeliveryStateStore inner)
        {
            _inner = inner;
        }

        public Func<UserDeliveryState, bool> FailWhen { get; set; } = _ => false;

        public Task<UserDeliveryState?> GetAsync(Guid userId, CancellationToken cancellationToken)
        {
            return _inner.GetAsync(userId, cancellationToken);
        }

        public Task<IReadOnlyList<UserDeliveryState>> GetAllAsync(CancellationToken cancellationToken)
        {
            return _inner.GetAllAsync(cancellationToken);
        }

        public Task SaveAsync(UserDeliveryState state, CancellationToken cancellationToken)
        {
            if (FailWhen(state))
            {
                throw new IOException("disk full");
            }

            return _inner.SaveAsync(state, cancellationToken);
        }

        public Task<DateTimeOffset?> GetLegacyCheckpointAsync(CancellationToken cancellationToken)
        {
            return _inner.GetLegacyCheckpointAsync(cancellationToken);
        }
    }
}
