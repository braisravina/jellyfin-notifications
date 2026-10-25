using System;
using System.IO;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Email;
using MimeKit;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Email;

public sealed class MimeMessageFactoryTests
{
    private static readonly SmtpSettings Settings = new()
    {
        Host = "smtp.example.com",
        Port = 587,
        Security = SmtpSecurityMode.StartTls,
        SenderAddress = "digest@example.com",
        SenderName = "Jellyfin Digest — Ñ"
    };

    private static OutgoingEmail Email(string subject = "Subject")
    {
        return new OutgoingEmail("ana@example.com", "Ana García", subject, "plain text", "<p>html</p>");
    }

    private static string Serialize(MimeMessage message)
    {
        using var stream = new MemoryStream();
        message.WriteTo(stream);
        return Encoding.ASCII.GetString(stream.ToArray());
    }

    [Fact]
    public void Create_BuildsMultipartAlternativeWithTextBeforeHtml()
    {
        var message = MimeMessageFactory.Create(Settings, Email());

        var alternative = Assert.IsType<MultipartAlternative>(message.Body);
        Assert.Equal("text/plain", alternative[0].ContentType.MimeType);
        Assert.Equal("text/html", alternative[1].ContentType.MimeType);
    }

    [Fact]
    public void Create_HasExactlyOneRecipientAndNoCcOrBcc()
    {
        var message = MimeMessageFactory.Create(Settings, Email());

        Assert.Equal("ana@example.com", Assert.Single(message.To.Mailboxes).Address);
        Assert.Empty(message.Cc);
        Assert.Empty(message.Bcc);
        Assert.Empty(message.ReplyTo);
    }

    [Fact]
    public void Create_NonAsciiSubjectAndNames_AreEncodedAsAsciiHeaders()
    {
        var wire = Serialize(MimeMessageFactory.Create(Settings, Email("Resumen — Amélie 千")));

        var headerEnd = wire.IndexOf("\n\n", StringComparison.Ordinal);
        Assert.True(headerEnd > 0);
        var headerBlock = wire[..headerEnd];
        Assert.All(headerBlock, c => Assert.True(c < 128));
        Assert.Contains("=?utf-8?", headerBlock, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_RoundTripsUtf8ThroughTheWireFormat()
    {
        var original = MimeMessageFactory.Create(Settings, Email("Resumen — Amélie 千"));

        using var stream = new MemoryStream();
        original.WriteTo(stream);
        stream.Position = 0;
        var parsed = MimeMessage.Load(stream);

        Assert.Equal("Resumen — Amélie 千", parsed.Subject);
        Assert.Equal("Ana García", parsed.To.Mailboxes.Single().Name);
        Assert.Equal("Jellyfin Digest — Ñ", parsed.From.Mailboxes.Single().Name);
    }

    [Fact]
    public void Create_SetsMessageIdDateAndAutoSubmitted()
    {
        var message = MimeMessageFactory.Create(Settings, Email());

        Assert.EndsWith("@example.com", message.MessageId, StringComparison.Ordinal);
        Assert.True(message.Date > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal("auto-generated", message.Headers["Auto-Submitted"]);
    }

    [Fact]
    public void Create_TwoMessages_HaveDifferentMessageIds()
    {
        Assert.NotEqual(
            MimeMessageFactory.Create(Settings, Email()).MessageId,
            MimeMessageFactory.Create(Settings, Email()).MessageId);
    }

    [Theory]
    [InlineData("Subject\r\nBcc: x@y.zz")]
    [InlineData("Subject\nX: y")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BadSubject_Throws(string subject)
    {
        Assert.Throws<ArgumentException>(() => MimeMessageFactory.Create(Settings, Email(subject)));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("a@b.co\r\nBcc: x@y.zz")]
    [InlineData("")]
    public void Create_BadRecipient_Throws(string recipient)
    {
        Assert.Throws<ArgumentException>(() => MimeMessageFactory.Create(Settings, new OutgoingEmail(recipient, "Ana", "S", "t", "h")));
    }

    [Fact]
    public void Create_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => MimeMessageFactory.Create(null!, Email()));
        Assert.Throws<ArgumentNullException>(() => MimeMessageFactory.Create(Settings, null!));
    }

    [Fact]
    public void SmtpSettings_ToString_NeverContainsThePasswordOrUsername()
    {
        var settings = new SmtpSettings { Host = "smtp.example.com", Port = 465, Username = "alice-the-user", Password = "hunter2-SECRET" };

        var text = settings.ToString();

        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", text, StringComparison.Ordinal);
        Assert.Contains("smtp.example.com:465", text, StringComparison.Ordinal);
    }
}
