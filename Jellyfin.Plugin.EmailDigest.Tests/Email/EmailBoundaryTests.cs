using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Digest;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Email;

/// <summary>
/// Boundaries: what the transport logs, and what it must never touch.
/// </summary>
public sealed class EmailBoundaryTests
{
    private const string Password = "Sup3r-S3cret-P4ss!";
    private const string Username = "svc-mailer-account";
    private const string Recipient = "private.person@example.com";
    private const string BodyMarker = "CONFIDENTIAL-TITLE-XYZ";

    private sealed class CapturingLogger<T> : ILogger<T>
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
            Lines.Enqueue(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
        }
    }

    private static OutgoingEmail Email()
    {
        return new OutgoingEmail(Recipient, "Private Person", "Subject " + BodyMarker, "text " + BodyMarker, "<p>" + BodyMarker + "</p>");
    }

    private static SmtpSettings Settings(FakeSmtpServer server, SmtpSecurityMode mode, string password)
    {
        return new SmtpSettings
        {
            Host = "127.0.0.1",
            Port = server.Port,
            Security = mode,
            Username = mode == SmtpSecurityMode.None ? string.Empty : Username,
            Password = password,
            SenderAddress = "digest@example.com",
            TimeoutSeconds = 2
        };
    }

    [Fact]
    public async Task Logs_NeverContainCredentialsRecipientOrContent_OnAnyOutcome()
    {
        var logger = new CapturingLogger<MailKitEmailTransport>();
        var transport = new MailKitEmailTransport(logger, (_, _, _, _) => true);

        using (var authFail = new FakeSmtpServer(implicitTls: true) { RequireAuth = true })
        {
            await transport.SendAsync(Settings(authFail, SmtpSecurityMode.ImplicitTls, Password), Email(), CancellationToken.None);
        }

        using (var accepted = new FakeSmtpServer(implicitTls: true) { RequireAuth = true, Password = Password, User = Username })
        {
            await transport.SendAsync(Settings(accepted, SmtpSecurityMode.ImplicitTls, Password), Email(), CancellationToken.None);
        }

        using (var rejected = new FakeSmtpServer { RejectRecipients = true })
        {
            await transport.SendAsync(Settings(rejected, SmtpSecurityMode.None, string.Empty), Email(), CancellationToken.None);
        }

        using (var tlsFail = new FakeSmtpServer { AdvertiseStartTls = false })
        {
            await transport.SendAsync(Settings(tlsFail, SmtpSecurityMode.StartTls, Password), Email(), CancellationToken.None);
        }

        var strict = new CapturingLogger<MailKitEmailTransport>();
        using (var untrusted = new FakeSmtpServer(implicitTls: true))
        {
            await new MailKitEmailTransport(strict).SendAsync(Settings(untrusted, SmtpSecurityMode.ImplicitTls, Password), Email(), CancellationToken.None);
        }

        var all = logger.Lines.Concat(strict.Lines).ToList();
        Assert.NotEmpty(all);
        foreach (var line in all)
        {
            Assert.DoesNotContain(Password, line, StringComparison.Ordinal);
            Assert.DoesNotContain(Username, line, StringComparison.Ordinal);
            Assert.DoesNotContain(Recipient, line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(BodyMarker, line, StringComparison.Ordinal);
            Assert.DoesNotContain("Private Person", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Result_ContainsNoFreeText()
    {
        var transport = new MailKitEmailTransport(new CapturingLogger<MailKitEmailTransport>(), (_, _, _, _) => true);
        using var server = new FakeSmtpServer { RejectRecipients = true };

        var result = await transport.SendAsync(Settings(server, SmtpSecurityMode.None, string.Empty), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.RecipientRejected, result.Outcome);
        Assert.DoesNotContain(Recipient, result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unknown", result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.All(typeof(EmailTransportResult).GetProperties(), p => Assert.NotEqual(typeof(string), p.PropertyType));
    }

    [Fact]
    public async Task Rendering_NeverTouchesTheNetwork()
    {
        using var server = new FakeSmtpServer();
        var item = new DiscoveredMediaItem { ItemId = Guid.NewGuid(), ItemType = DiscoveredMediaItemType.Movie, Name = "Heat", Category = MediaCategory.Movies };

        var model = new DigestBuilder().Build(new[] { item }, DigestOptions.Default);
        new DigestRenderer(new LocalizationService()).Render(model, new DigestRecipient("Ana", "en"));
        await Task.Delay(100);

        Assert.Empty(server.Commands);
        Assert.Empty(server.Messages);
    }

    [Fact]
    public void Renderer_AndBuilder_HaveNoDependencyOnTheTransportOrPasswordStore()
    {
        foreach (var type in new[] { typeof(DigestRenderer), typeof(DigestBuilder), typeof(DigestText) })
        {
            var dependencies = type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);
            Assert.DoesNotContain(dependencies, t => t == typeof(IEmailTransport) || t == typeof(ISmtpPasswordStore));
        }
    }

    [Fact]
    public void Transport_HasNoDependencyOnCheckpointsPreferencesOrDiscovery()
    {
        var dependencies = typeof(MailKitEmailTransport).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        Assert.DoesNotContain(dependencies, t => t == typeof(ICheckpointService) || t == typeof(IUserPreferenceRepository) || t == typeof(IMediaDiscoveryService));
        Assert.DoesNotContain(typeof(IEmailTransport).GetMethods().SelectMany(m => m.GetParameters()), p => p.ParameterType == typeof(Guid));
    }

    [Fact]
    public async Task Sending_LeavesTheCheckpointAndPreferenceFilesUntouched()
    {
        var folder = Path.Combine(Path.GetTempPath(), "emaildigest-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var checkpoint = new CheckpointService(folder);
            var before = await checkpoint.GetLastSuccessfulRunAsync(CancellationToken.None);
            var transport = new MailKitEmailTransport(new CapturingLogger<MailKitEmailTransport>(), (_, _, _, _) => true);
            using var server = new FakeSmtpServer();

            var result = await transport.SendAsync(Settings(server, SmtpSecurityMode.None, string.Empty), Email(), CancellationToken.None);

            Assert.True(result.IsAccepted);
            Assert.Equal(before, await checkpoint.GetLastSuccessfulRunAsync(CancellationToken.None));
            Assert.Empty(Directory.GetFileSystemEntries(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
