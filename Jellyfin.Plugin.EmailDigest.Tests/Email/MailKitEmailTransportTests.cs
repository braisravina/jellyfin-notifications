using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Email;

public sealed class MailKitEmailTransportTests
{
    // The fake server uses a self-signed certificate; tests that need a successful TLS handshake
    // trust it explicitly. The default (production) transport trusts nothing extra.
    private static readonly MailKitEmailTransport Trusting = new(NullLogger<MailKitEmailTransport>.Instance, (_, _, _, _) => true);
    private static readonly MailKitEmailTransport Strict = new(NullLogger<MailKitEmailTransport>.Instance);

    private static SmtpSettings Settings(FakeSmtpServer server, SmtpSecurityMode mode, string user = "", string? password = null, int timeout = 5)
    {
        return new SmtpSettings
        {
            Host = "127.0.0.1",
            Port = server.Port,
            Security = mode,
            Username = user,
            Password = password,
            SenderAddress = "digest@example.com",
            SenderName = "Jellyfin",
            TimeoutSeconds = timeout
        };
    }

    private static OutgoingEmail Email(string to = "ana@example.com", string name = "Ana", string subject = "Jellyfin digest: 2 new items")
    {
        return new OutgoingEmail(to, name, subject, "Hello Ana,\n\nAmélie (2001)\n", "<html><body><p>Hello Ana, Amélie (2001)</p></body></html>");
    }

    // Success paths

    [Fact]
    public async Task Send_Unencrypted_IsAcceptedAndMessageArrivesIntact()
    {
        using var server = new FakeSmtpServer();

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.Accepted, result.Outcome);
        var received = Assert.Single(server.Messages);
        Assert.False(received.WasEncrypted);
        Assert.Contains("digest@example.com", received.MailFrom, StringComparison.Ordinal);
        Assert.Equal("RCPT TO:<ana@example.com>", Assert.Single(received.RcptTo));
    }

    [Fact]
    public async Task Send_StartTls_UpgradesBeforeAnythingIsSent()
    {
        using var server = new FakeSmtpServer { AdvertiseStartTls = true };

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.StartTls), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.Accepted, result.Outcome);
        Assert.True(Assert.Single(server.Messages).WasEncrypted);
        var commands = server.Commands.ToList();
        Assert.True(commands.FindIndex(c => c == "STARTTLS") < commands.FindIndex(c => c.StartsWith("MAIL FROM", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Send_ImplicitTls_IsEncryptedFromTheFirstByte()
    {
        using var server = new FakeSmtpServer(implicitTls: true);

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.ImplicitTls), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.Accepted, result.Outcome);
        Assert.True(Assert.Single(server.Messages).WasEncrypted);
    }

    [Fact]
    public async Task Send_WithCorrectCredentialsOverTls_Authenticates()
    {
        using var server = new FakeSmtpServer(implicitTls: true) { RequireAuth = true };

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.ImplicitTls, "mailer", "correct-horse"), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.Accepted, result.Outcome);
        Assert.Single(server.Messages);
    }

    [Fact]
    public async Task TestConnection_ConnectsAndLogsInButSendsNoMessage()
    {
        using var server = new FakeSmtpServer(implicitTls: true) { RequireAuth = true };

        var result = await Trusting.TestConnectionAsync(Settings(server, SmtpSecurityMode.ImplicitTls, "mailer", "correct-horse"), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.Accepted, result.Outcome);
        Assert.Empty(server.Messages);
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("MAIL FROM", StringComparison.Ordinal) || c.StartsWith("RCPT TO", StringComparison.Ordinal) || c == "DATA");
    }

    // TLS enforcement

    [Fact]
    public async Task Send_ImplicitTlsWithUntrustedCertificate_FailsAndSendsNothing()
    {
        using var server = new FakeSmtpServer(implicitTls: true);

        var result = await Strict.SendAsync(Settings(server, SmtpSecurityMode.ImplicitTls), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.TlsFailed, result.Outcome);
        Assert.Empty(server.Messages);
    }

    [Fact]
    public async Task Send_StartTlsWithUntrustedCertificate_FailsAndSendsNothing()
    {
        using var server = new FakeSmtpServer { AdvertiseStartTls = true };

        var result = await Strict.SendAsync(Settings(server, SmtpSecurityMode.StartTls), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.TlsFailed, result.Outcome);
        Assert.Empty(server.Messages);
    }

    [Fact]
    public async Task Send_StartTlsRequestedButServerDoesNotOfferIt_FailsInsteadOfDowngrading()
    {
        using var server = new FakeSmtpServer { AdvertiseStartTls = false };

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.StartTls), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.TlsFailed, result.Outcome);
        Assert.Empty(server.Messages);
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("MAIL FROM", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Send_ImplicitTlsAgainstAPlainTextServer_Fails()
    {
        using var server = new FakeSmtpServer();

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.ImplicitTls), Email(), CancellationToken.None);

        Assert.NotEqual(EmailTransportOutcome.Accepted, result.Outcome);
        Assert.Empty(server.Messages);
    }

    [Fact]
    public async Task Send_CredentialsWithoutEncryption_AreRefusedBeforeConnecting()
    {
        using var server = new FakeSmtpServer { RequireAuth = true };

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None, "mailer", "correct-horse"), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.InvalidRequest, result.Outcome);
        Assert.Empty(server.Commands);
    }

    // Failures

    [Fact]
    public async Task Send_WrongPassword_ReportsAuthenticationFailed()
    {
        using var server = new FakeSmtpServer(implicitTls: true) { RequireAuth = true };

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.ImplicitTls, "mailer", "wrong"), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.AuthenticationFailed, result.Outcome);
        Assert.Empty(server.Messages);
    }

    [Fact]
    public async Task Send_UsernameButNoPassword_IsInvalidNotAnAttempt()
    {
        using var server = new FakeSmtpServer(implicitTls: true) { RequireAuth = true };

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.ImplicitTls, "mailer", null), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.InvalidRequest, result.Outcome);
        Assert.Empty(server.Commands);
    }

    [Fact]
    public async Task Send_RecipientRejected_ReportsRecipientRejectedWithStatusCode()
    {
        using var server = new FakeSmtpServer { RejectRecipients = true };

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.RecipientRejected, result.Outcome);
        Assert.Equal(550, result.SmtpStatusCode);
        Assert.Empty(server.Messages);
    }

    [Fact]
    public async Task Send_SenderRejected_ReportsSenderRejected()
    {
        using var server = new FakeSmtpServer { RejectSender = true };

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.SenderRejected, result.Outcome);
    }

    [Fact]
    public async Task Send_NothingListening_ReportsConnectionFailed()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        var settings = new SmtpSettings { Host = "127.0.0.1", Port = port, Security = SmtpSecurityMode.None, SenderAddress = "a@example.com", TimeoutSeconds = 5 };

        var result = await Trusting.SendAsync(settings, Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.ConnectionFailed, result.Outcome);
    }

    [Fact]
    public async Task Send_ServerNeverGreets_TimesOutWithinTheBudget()
    {
        using var server = new FakeSmtpServer { HangBeforeGreeting = true };
        var started = DateTime.UtcNow;

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None, timeout: 1), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.Timeout, result.Outcome);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Send_CancelledBeforeStart_ReportsCancelledWithoutConnecting()
    {
        using var server = new FakeSmtpServer();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), Email(), cts.Token);

        Assert.Equal(EmailTransportOutcome.Cancelled, result.Outcome);
        Assert.Empty(server.Commands);
    }

    [Fact]
    public async Task Send_CancelledWhileWaiting_ReportsCancelledNotTimeout()
    {
        using var server = new FakeSmtpServer { HangBeforeGreeting = true };
        using var cts = new CancellationTokenSource();
        var task = Trusting.SendAsync(Settings(server, SmtpSecurityMode.None, timeout: 30), Email(), cts.Token);
        await Task.Delay(300);

        await cts.CancelAsync();
        var result = await task;

        Assert.Equal(EmailTransportOutcome.Cancelled, result.Outcome);
    }

    // Uncertain outcomes: the connection fails after sending began, so acceptance is unknown.

    [Fact]
    public async Task Send_ReplyToTheDataCommandIsLost_ReportsUncertainEvenThoughTheServerGotTheMessage()
    {
        using var server = new FakeSmtpServer { DropAfterData = true };

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.Uncertain, result.Outcome);
        Assert.False(result.IsAccepted);
        Assert.Single(server.Messages);
    }

    [Fact]
    public async Task Send_ServerStopsAnsweringAfterTheData_ReportsUncertainNotTimeout()
    {
        using var server = new FakeSmtpServer { HangAfterData = true };

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None, timeout: 1), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.Uncertain, result.Outcome);
    }

    [Fact]
    public async Task Send_ConnectionDropsWhileSending_IsConservativelyUncertain()
    {
        using var server = new FakeSmtpServer { DropAtRecipient = true };

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.Uncertain, result.Outcome);
        Assert.Empty(server.Messages);
    }

    [Fact]
    public async Task Send_ConnectionDropsBeforeAnythingIsSent_IsAPlainConnectionFailure()
    {
        using var server = new FakeSmtpServer { DropAtGreeting = true };

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.ConnectionFailed, result.Outcome);
        Assert.Empty(server.Messages);
    }

    [Fact]
    public async Task TestConnection_NeverReportsUncertain()
    {
        using var server = new FakeSmtpServer { HangBeforeGreeting = true };

        var result = await Trusting.TestConnectionAsync(Settings(server, SmtpSecurityMode.None, timeout: 1), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.Timeout, result.Outcome);
    }

    // Deterministic Message-ID

    [Fact]
    public async Task Send_WithAMessageIdPart_PutsItOnTheWireOnTheSendersDomain()
    {
        using var server = new FakeSmtpServer();
        var email = Email() with { MessageIdLocalPart = "emaildigest-0123456789abcdef0123456789abcdef" };

        await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), email, CancellationToken.None);
        await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), email, CancellationToken.None);

        var ids = server.Messages.Select(m => Parse(m.Raw).MessageId).ToList();
        Assert.Equal(2, ids.Count);
        Assert.All(ids, id => Assert.Equal("emaildigest-0123456789abcdef0123456789abcdef@example.com", id));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("has space in it")]
    [InlineData("bad<chars>here")]
    [InlineData("line\nbreak-0123456789")]
    public async Task Send_InvalidMessageIdPart_IsRejectedBeforeConnecting(string part)
    {
        using var server = new FakeSmtpServer();

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), Email() with { MessageIdLocalPart = part }, CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.InvalidRequest, result.Outcome);
        Assert.Empty(server.Commands);
    }

    // Request validation

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("ana@example.com\r\nBcc: boss@example.com")]
    [InlineData("")]
    public async Task Send_InvalidRecipient_IsRejectedBeforeConnecting(string recipient)
    {
        using var server = new FakeSmtpServer();

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), Email(to: recipient), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.InvalidRequest, result.Outcome);
        Assert.Empty(server.Commands);
    }

    [Theory]
    [InlineData("Subject\r\nBcc: boss@example.com")]
    [InlineData("")]
    public async Task Send_InvalidSubject_IsRejectedBeforeConnecting(string subject)
    {
        using var server = new FakeSmtpServer();

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), Email(subject: subject), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.InvalidRequest, result.Outcome);
        Assert.Empty(server.Commands);
    }

    [Fact]
    public async Task Send_RecipientNameWithLineBreak_IsRejected()
    {
        using var server = new FakeSmtpServer();

        var result = await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), Email(name: "Ana\r\nBcc: x@y.zz"), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.InvalidRequest, result.Outcome);
    }

    [Theory]
    [InlineData("", 25, 5)]
    [InlineData("smtp.example.com", 0, 5)]
    [InlineData("smtp.example.com", 70000, 5)]
    [InlineData("smtp.example.com", 25, 0)]
    [InlineData("smtp.example.com", 25, 301)]
    [InlineData("smtp example.com", 25, 5)]
    public async Task Send_InvalidSettings_AreRejectedWithoutNetworkActivity(string host, int port, int timeout)
    {
        var settings = new SmtpSettings { Host = host, Port = port, Security = SmtpSecurityMode.None, SenderAddress = "a@example.com", TimeoutSeconds = timeout };

        var result = await Trusting.SendAsync(settings, Email(), CancellationToken.None);

        Assert.Equal(EmailTransportOutcome.InvalidRequest, result.Outcome);
    }

    [Fact]
    public async Task Send_UnknownSecurityMode_IsRejected()
    {
        using var server = new FakeSmtpServer();
        var settings = Settings(server, (SmtpSecurityMode)99);

        Assert.Equal(EmailTransportOutcome.InvalidRequest, (await Trusting.SendAsync(settings, Email(), CancellationToken.None)).Outcome);
    }

    // Isolation, wire format

    [Fact]
    public async Task Send_TwoUsers_AreTwoSeparateMessagesEachWithOneRecipientAndNoCrossover()
    {
        using var server = new FakeSmtpServer();
        var settings = Settings(server, SmtpSecurityMode.None);

        await Trusting.SendAsync(settings, Email("ana@example.com", "Ana"), CancellationToken.None);
        await Trusting.SendAsync(settings, Email("breogan@example.org", "Breogán"), CancellationToken.None);

        var messages = server.Messages.ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal("RCPT TO:<ana@example.com>", Assert.Single(messages[0].RcptTo));
        Assert.Equal("RCPT TO:<breogan@example.org>", Assert.Single(messages[1].RcptTo));

        var first = Parse(messages[0].Raw);
        var second = Parse(messages[1].Raw);
        Assert.Equal("ana@example.com", Assert.Single(first.To.Mailboxes).Address);
        Assert.Equal("breogan@example.org", Assert.Single(second.To.Mailboxes).Address);
        Assert.Empty(first.Cc);
        Assert.Empty(first.Bcc);
        Assert.DoesNotContain("breogan", System.Text.Encoding.Latin1.GetString(messages[0].Raw), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ana@example.com", System.Text.Encoding.Latin1.GetString(messages[1].Raw), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Send_OnTheWire_IsMultipartAlternativeTextFirstThenHtmlAndUtf8()
    {
        using var server = new FakeSmtpServer();

        await Trusting.SendAsync(Settings(server, SmtpSecurityMode.None), Email(subject: "Resumen de Jellyfin: 2 novedades — Amélie 千と千尋"), CancellationToken.None);

        var message = Parse(Assert.Single(server.Messages).Raw);
        var alternative = Assert.IsType<MultipartAlternative>(message.Body);
        Assert.Equal(2, alternative.Count);
        var text = Assert.IsType<TextPart>(alternative[0]);
        var html = Assert.IsType<TextPart>(alternative[1]);
        Assert.True(text.IsPlain);
        Assert.True(html.IsHtml);
        Assert.Equal("utf-8", text.ContentType.Charset);
        Assert.Contains("Amélie (2001)", text.Text, StringComparison.Ordinal);
        Assert.Contains("Amélie (2001)", html.Text, StringComparison.Ordinal);
        Assert.Equal("Resumen de Jellyfin: 2 novedades — Amélie 千と千尋", message.Subject);
    }

    private static MimeMessage Parse(byte[] raw)
    {
        using var stream = new MemoryStream(raw);
        return MimeMessage.Load(stream);
    }
}
