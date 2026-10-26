using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Admin;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Digest;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Localization;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Runs one digest delivery: for each eligible user, finds what is new to them, builds and renders a
/// digest, hands one message to SMTP, and records the delivery only when the server confirmed it.
/// </summary>
/// <remarks>
/// <para>
/// Guarantees. A user's checkpoint moves only after the SMTP server accepted that user's message, and
/// only that user's. Any failure, uncertainty, cancellation or error leaves it where it was, and other
/// users are unaffected. Items hidden by a display limit stay pending. The upper end of the media window
/// is fixed once per run (a few minutes in the past, so media still being added is not skipped), and
/// media created later falls into the next run. A disabled digest, an overlapping run, invalid
/// configuration, an unreadable state file or an unusable SMTP login stop the run before anything is
/// sent or advanced. Dry-run mode never sends and never records.
/// </para>
/// <para>
/// Not exactly-once. SMTP cannot offer that: if the server accepts a message and the reply is lost, the
/// run records nothing and the next run resends, so a duplicate is possible. The same applies if
/// recording a confirmed delivery fails. The message id is the same for a resend of the same digest.
/// </para>
/// </remarks>
public sealed class DigestRunner
{
    /// <summary>
    /// How far behind "now" the upper bound of the media window is placed, so items whose creation
    /// is still being saved by a library scan are not skipped.
    /// </summary>
    public static readonly TimeSpan SettleMargin = TimeSpan.FromMinutes(5);

    private readonly ISmtpConfigurationAccessor _configuration;
    private readonly IMonitoredLibraryValidator _libraries;
    private readonly IUserPreferenceRepository _preferences;
    private readonly IUserDirectory _users;
    private readonly IMediaDiscoveryService _discovery;
    private readonly IDigestBuilder _builder;
    private readonly IDigestRenderer _renderer;
    private readonly IEmailTransport _transport;
    private readonly ISmtpPasswordStore _passwords;
    private readonly IDeliveryStateStore _state;
    private readonly IRunSummaryStore _summaries;
    private readonly IDigestRunLock _lock;
    private readonly DigestRetryPolicy _retry;
    private readonly TimeProvider _time;
    private readonly ILogger<DigestRunner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DigestRunner"/> class.
    /// </summary>
    /// <param name="configuration">Configuration access.</param>
    /// <param name="libraries">The monitored-library validator.</param>
    /// <param name="preferences">Per-user preferences.</param>
    /// <param name="users">Jellyfin user lookup.</param>
    /// <param name="discovery">Media discovery.</param>
    /// <param name="builder">The digest builder.</param>
    /// <param name="renderer">The digest renderer.</param>
    /// <param name="transport">The SMTP transport.</param>
    /// <param name="passwords">The SMTP password store.</param>
    /// <param name="state">Per-user delivery state.</param>
    /// <param name="summaries">The last-run summary store.</param>
    /// <param name="runLock">The run lock.</param>
    /// <param name="retry">The retry policy.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public DigestRunner(
        ISmtpConfigurationAccessor configuration,
        IMonitoredLibraryValidator libraries,
        IUserPreferenceRepository preferences,
        IUserDirectory users,
        IMediaDiscoveryService discovery,
        IDigestBuilder builder,
        IDigestRenderer renderer,
        IEmailTransport transport,
        ISmtpPasswordStore passwords,
        IDeliveryStateStore state,
        IRunSummaryStore summaries,
        IDigestRunLock runLock,
        DigestRetryPolicy retry,
        TimeProvider time,
        ILogger<DigestRunner> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _libraries = libraries ?? throw new ArgumentNullException(nameof(libraries));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _passwords = passwords ?? throw new ArgumentNullException(nameof(passwords));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));
        _lock = runLock ?? throw new ArgumentNullException(nameof(runLock));
        _retry = retry ?? throw new ArgumentNullException(nameof(retry));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private enum UserResult
    {
        Accepted,
        AcceptedNotRecorded,
        DryRunRendered,
        NoNewMedia,
        BaselineRecorded,
        Skipped,
        Failed,
        Uncertain,
        StopRun
    }

    /// <summary>
    /// Runs one delivery. Scheduled and manual runs both come through here.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The summary of the run.</returns>
    public async Task<DigestRunSummary> RunAsync(CancellationToken cancellationToken)
    {
        var started = _time.GetUtcNow();
        var summary = new DigestRunSummary { StartedUtc = started, UpperBoundUtc = started - SettleMargin };

        PluginConfiguration config;
        try
        {
            config = _configuration.GetCopy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("The Email Digest run could not read its configuration ({ExceptionType}).", ex.GetType().Name);
            return await FinishAsync(summary, DigestRunOutcome.Aborted, "configuration-unreadable").ConfigureAwait(false);
        }

        if (!config.DigestEnabled)
        {
            _logger.LogInformation("The Email Digest is disabled; the run did nothing.");
            summary.Mode = DigestRunMode.Disabled;
            return await FinishAsync(summary, DigestRunOutcome.Skipped, "disabled", persist: false).ConfigureAwait(false);
        }

        summary.Mode = config.DigestDryRun ? DigestRunMode.DryRun : DigestRunMode.Live;

        using var handle = _lock.TryAcquire();
        if (handle is null)
        {
            _logger.LogWarning("Another Email Digest run is already in progress; this run did nothing.");
            return await FinishAsync(summary, DigestRunOutcome.Skipped, "already-running", persist: false).ConfigureAwait(false);
        }

        _logger.LogInformation("Email Digest run started in {Mode} mode.", summary.Mode);

        try
        {
            return await ExecuteAsync(config, summary, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("The Email Digest run was cancelled.");
            return await FinishAsync(summary, DigestRunOutcome.Cancelled, "cancelled").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError("The Email Digest run failed unexpectedly ({ExceptionType}).", ex.GetType().Name);
            return await FinishAsync(summary, DigestRunOutcome.Aborted, "unexpected-error").ConfigureAwait(false);
        }
    }

    private async Task<DigestRunSummary> ExecuteAsync(PluginConfiguration config, DigestRunSummary summary, CancellationToken cancellationToken)
    {
        var errors = PluginConfigurationValidator.Validate(config);
        if (errors.Count > 0)
        {
            _logger.LogError("The Email Digest configuration is invalid ({ErrorCount} problem(s)); nothing was done.", errors.Count);
            return await FinishAsync(summary, DigestRunOutcome.Aborted, "configuration-invalid").ConfigureAwait(false);
        }

        if (config.MonitoredLibraries.Count == 0)
        {
            _logger.LogWarning("No libraries are monitored; the run did nothing.");
            return await FinishAsync(summary, DigestRunOutcome.Skipped, "no-libraries").ConfigureAwait(false);
        }

        IReadOnlyList<MonitoredLibraryIssue> issues;
        try
        {
            issues = _libraries.Validate(config.MonitoredLibraries.ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("The monitored libraries could not be verified ({ExceptionType}); nothing was done.", ex.GetType().Name);
            return await FinishAsync(summary, DigestRunOutcome.Aborted, "libraries-unverifiable").ConfigureAwait(false);
        }

        if (issues.Any(i => i.Kind != MonitoredLibraryIssueKind.Duplicate))
        {
            _logger.LogError("{Count} monitored librar(ies) no longer exist or are invalid; nothing was done.", issues.Count);
            return await FinishAsync(summary, DigestRunOutcome.Aborted, "libraries-invalid").ConfigureAwait(false);
        }

        SmtpSettings? smtp = null;
        if (summary.Mode == DigestRunMode.Live)
        {
            if (!config.SmtpEnabled)
            {
                _logger.LogError("SMTP delivery is not enabled; a live run cannot send. Enable SMTP or use dry run.");
                return await FinishAsync(summary, DigestRunOutcome.Aborted, "smtp-disabled").ConfigureAwait(false);
            }

            var password = await _passwords.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(config.SmtpUsername) && string.IsNullOrEmpty(password))
            {
                _logger.LogError("An SMTP username is set but no password is available; nothing was sent.");
                return await FinishAsync(summary, DigestRunOutcome.Aborted, "smtp-password-missing").ConfigureAwait(false);
            }

            smtp = SmtpSettings.From(config, password);
        }

        Dictionary<Guid, UserDeliveryState> states;
        DateTimeOffset? legacy;
        try
        {
            states = (await _state.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.UserId);
            legacy = await _state.GetLegacyCheckpointAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DeliveryStateException)
        {
            _logger.LogError("The delivery state file cannot be trusted; nothing was sent. See the documentation to recover it.");
            return await FinishAsync(summary, DigestRunOutcome.Aborted, "state-unreadable").ConfigureAwait(false);
        }

        List<UserNotificationPreferences> preferences;
        try
        {
            preferences = (await _preferences.GetAllAsync(cancellationToken).ConfigureAwait(false)).OrderBy(p => p.UserId).ToList();
        }
        catch (UserPreferencesException)
        {
            _logger.LogError("The user preferences file cannot be trusted; nothing was sent. See the documentation to recover it.");
            return await FinishAsync(summary, DigestRunOutcome.Aborted, "preferences-unreadable").ConfigureAwait(false);
        }

        // Only a user who wants digests AND has a verified address is eligible; a pending or
        // unverified address is never used.
        var eligible = preferences.Where(p => p.CanReceiveDigest()).ToList();
        summary.UsersSkipped += preferences.Count - eligible.Count;

        var stopReason = string.Empty;
        for (var index = 0; index < eligible.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preference = eligible[index];
            summary.UsersEvaluated++;

            var result = await ProcessUserAsync(config, smtp, preference, states, legacy, summary, cancellationToken).ConfigureAwait(false);
            switch (result)
            {
                case UserResult.Accepted:
                    summary.MessagesAccepted++;
                    break;
                case UserResult.AcceptedNotRecorded:
                    summary.MessagesAccepted++;
                    summary.AcceptedButNotRecorded++;
                    stopReason = "state-write-failed";
                    break;
                case UserResult.DryRunRendered:
                    summary.DigestsRenderedDryRun++;
                    break;
                case UserResult.NoNewMedia:
                    summary.UsersWithNoNewMedia++;
                    break;
                case UserResult.BaselineRecorded:
                    summary.BaselinesRecorded++;
                    break;
                case UserResult.Skipped:
                    summary.UsersSkipped++;
                    break;
                case UserResult.Uncertain:
                    summary.MessagesFailed++;
                    summary.MessagesUncertain++;
                    break;
                case UserResult.StopRun:
                    summary.MessagesFailed++;
                    stopReason = "smtp-unusable";
                    break;
                default:
                    summary.MessagesFailed++;
                    break;
            }

            if (stopReason.Length > 0)
            {
                summary.UsersNotAttempted = eligible.Count - index - 1;
                _logger.LogError("The Email Digest run stopped early ({Reason}); {Count} user(s) were not attempted.", stopReason, summary.UsersNotAttempted);
                return await FinishAsync(summary, DigestRunOutcome.Aborted, stopReason).ConfigureAwait(false);
            }
        }

        var failed = summary.MessagesFailed > 0;
        return await FinishAsync(summary, failed ? DigestRunOutcome.CompletedWithFailures : DigestRunOutcome.Completed, failed ? "user-failures" : string.Empty).ConfigureAwait(false);
    }

    private async Task<UserResult> ProcessUserAsync(
        PluginConfiguration config,
        SmtpSettings? smtp,
        UserNotificationPreferences preference,
        Dictionary<Guid, UserDeliveryState> states,
        DateTimeOffset? legacy,
        DigestRunSummary summary,
        CancellationToken cancellationToken)
    {
        var userId = preference.UserId;
        var live = summary.Mode == DigestRunMode.Live;

        var user = _users.Find(userId);
        if (user is null || user.IsDisabled)
        {
            _logger.LogInformation("A user was skipped: {Reason}.", user is null ? "no such Jellyfin user" : "account disabled");
            return UserResult.Skipped;
        }

        if (!ConfigurationValidation.IsValidEmail(preference.Email) || !ConfigurationValidation.IsValidSingleLine(preference.Email, 254))
        {
            _logger.LogWarning("A user was skipped: the stored email address is not valid.");
            return UserResult.Skipped;
        }

        var language = SupportedLanguages.IsSupported(preference.Language) ? preference.Language : config.DefaultLanguage;
        states.TryGetValue(userId, out var state);
        var window = DeliveryWindowPlanner.Plan(state, legacy, config.FallbackLookbackHours, summary.UpperBoundUtc);

        if (window.IsEmpty)
        {
            // A new user with no lookback: remember "from now" so their first real digest is not the whole library.
            if (state is null && live)
            {
                return await TryRecordAsync(new UserDeliveryState { UserId = userId, CheckpointUtc = window.ToUtc }, states, cancellationToken).ConfigureAwait(false)
                    ? UserResult.BaselineRecorded
                    : UserResult.Failed;
            }

            return UserResult.NoNewMedia;
        }

        IReadOnlyList<DiscoveredMediaItem> discovered;
        try
        {
            discovered = await _discovery.DiscoverAsync(userId, config.MonitoredLibraries.ToList(), window.FromUtc, window.ToUtc, cancellationToken).ConfigureAwait(false);
        }
        catch (MediaDiscoveryException)
        {
            _logger.LogWarning("Discovery failed for a user; their checkpoint was not changed.");
            return UserResult.Failed;
        }

        var alreadyDelivered = state?.Delivered.Select(d => d.ItemId).ToHashSet() ?? new HashSet<Guid>();
        var pending = discovered.Where(i => !alreadyDelivered.Contains(i.ItemId)).ToList();

        if (pending.Count == 0)
        {
            if (!live)
            {
                return UserResult.NoNewMedia;
            }

            // Everything in the window was examined and nothing is left to tell this user.
            var examined = state ?? new UserDeliveryState { UserId = userId };
            examined.CheckpointUtc = window.ToUtc;
            return await TryRecordAsync(examined, states, cancellationToken).ConfigureAwait(false) ? UserResult.NoNewMedia : UserResult.Failed;
        }

        DigestModel model;
        RenderedDigest rendered;
        try
        {
            model = _builder.Build(pending, DigestOptions.From(config));
            rendered = _renderer.Render(model, new DigestRecipient(user.DisplayName, language));
        }
        catch (Exception ex) when (ex is DigestRenderException or ArgumentException or InvalidOperationException)
        {
            _logger.LogError("A digest could not be built or rendered ({ExceptionType}); nothing was sent.", ex.GetType().Name);
            return UserResult.Failed;
        }

        if (!live)
        {
            _logger.LogInformation("Dry run: a digest of {Items} item(s) was built and rendered for a user; nothing was sent.", pending.Count);
            return UserResult.DryRunRendered;
        }

        var shown = model.Sections.SelectMany(section => section.Entries).SelectMany(entry => entry.ItemIds).ToHashSet();
        var email = new OutgoingEmail(
            preference.Email,
            user.DisplayName,
            rendered.Subject,
            rendered.TextBody,
            rendered.HtmlBody,
            DigestMessageId.Create(userId, window, shown));

        var delivery = await SendWithRetryAsync(smtp!, email, cancellationToken).ConfigureAwait(false);
        switch (delivery)
        {
            case DeliveryFailureKind.Accepted:
                break;
            case DeliveryFailureKind.StopRun:
                return UserResult.StopRun;
            case DeliveryFailureKind.Uncertain:
                _logger.LogWarning("Delivery to a user is uncertain; their checkpoint was not changed and a later run may resend (possible duplicate).");
                return UserResult.Uncertain;
            case DeliveryFailureKind.Cancelled:
                cancellationToken.ThrowIfCancellationRequested();
                return UserResult.Failed;
            default:
                return UserResult.Failed;
        }

        // Confirmed by the SMTP server: now, and only now, record it for this user.
        var now = _time.GetUtcNow();
        var updated = state ?? new UserDeliveryState { UserId = userId };
        foreach (var id in shown)
        {
            updated.Delivered.Add(new DeliveredItem { ItemId = id, DeliveredUtc = now });
        }

        var hidden = pending.Where(i => !shown.Contains(i.ItemId)).ToList();
        updated.CheckpointUtc = hidden.Count == 0
            ? window.ToUtc
            : Max(window.FromUtc, hidden.Min(i => i.DateAddedUtc) - TimeSpan.FromTicks(1));
        updated.LastOutcome = "Accepted";

        return await TryRecordAsync(updated, states, cancellationToken).ConfigureAwait(false) ? UserResult.Accepted : UserResult.AcceptedNotRecorded;
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b)
    {
        return a >= b ? a : b;
    }

    private async Task<bool> TryRecordAsync(UserDeliveryState state, Dictionary<Guid, UserDeliveryState> states, CancellationToken cancellationToken)
    {
        try
        {
            await _state.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            states[state.UserId] = state;
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or DeliveryStateException)
        {
            _logger.LogError("Recording a delivery failed ({ExceptionType}); the user may receive a duplicate on the next run.", ex.GetType().Name);
            return false;
        }
    }

    private async Task<DeliveryFailureKind> SendWithRetryAsync(SmtpSettings smtp, OutgoingEmail email, CancellationToken cancellationToken)
    {
        var kind = DeliveryFailureKind.Transient;
        for (var attempt = 1; attempt <= _retry.MaxAttempts; attempt++)
        {
            var attemptId = Guid.NewGuid().ToString("N");
            var result = await _transport.SendAsync(smtp, email, cancellationToken).ConfigureAwait(false);
            kind = DeliveryFailureClassifier.Classify(result);
            _logger.LogInformation("Delivery attempt {AttemptId} ({Attempt}/{Max}): {Outcome}.", attemptId, attempt, _retry.MaxAttempts, result.Outcome);

            if (kind != DeliveryFailureKind.Transient || attempt == _retry.MaxAttempts)
            {
                return kind;
            }

            await Task.Delay(_retry.DelayBefore(attempt), cancellationToken).ConfigureAwait(false);
        }

        return kind;
    }

    private async Task<DigestRunSummary> FinishAsync(DigestRunSummary summary, DigestRunOutcome outcome, string reason, bool persist = true)
    {
        summary.Outcome = outcome;
        summary.Reason = reason;
        summary.FinishedUtc = _time.GetUtcNow();

        if (outcome is DigestRunOutcome.CompletedWithFailures or DigestRunOutcome.Aborted)
        {
            _logger.LogWarning(
                "Email Digest run ended: {Outcome} ({Reason}). Users evaluated {Evaluated}, skipped {Skipped}, accepted {Accepted}, failed {Failed}, uncertain {Uncertain}, no new media {NoNew}, not attempted {NotAttempted}.",
                outcome,
                reason,
                summary.UsersEvaluated,
                summary.UsersSkipped,
                summary.MessagesAccepted,
                summary.MessagesFailed,
                summary.MessagesUncertain,
                summary.UsersWithNoNewMedia,
                summary.UsersNotAttempted);
        }
        else
        {
            _logger.LogInformation(
                "Email Digest run ended: {Outcome} ({Reason}). Users evaluated {Evaluated}, skipped {Skipped}, accepted {Accepted}, no new media {NoNew}, dry-run digests {DryRun}, baselines {Baselines}.",
                outcome,
                reason,
                summary.UsersEvaluated,
                summary.UsersSkipped,
                summary.MessagesAccepted,
                summary.UsersWithNoNewMedia,
                summary.DigestsRenderedDryRun,
                summary.BaselinesRecorded);
        }

        if (persist)
        {
            try
            {
                await _summaries.SaveAsync(summary, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("The run summary could not be saved ({ExceptionType}).", ex.GetType().Name);
            }
        }

        return summary;
    }
}
