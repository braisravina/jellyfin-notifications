using System;
using System.Linq;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Digest;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Email;

public sealed class UnsubscribeHeaderTests
{
    private static readonly SmtpSettings Settings = new() { Host = "smtp.example.com", Port = 587, SenderAddress = "digest@example.com", SenderName = "Jellyfin" };

    private static OutgoingEmail Mail(string? url) => new("ana@example.com", "Ana", "Subject", "Text", "<p>Html</p>", null, url);

    [Fact]
    public void Message_WithALink_CarriesTheStandardOneClickHeaders()
    {
        var message = MimeMessageFactory.Create(Settings, Mail("https://jellyfin.example.org/EmailDigest/Unsubscribe?token=abc"));

        Assert.Equal("<https://jellyfin.example.org/EmailDigest/Unsubscribe?token=abc>", message.Headers["List-Unsubscribe"]);
        Assert.Equal("List-Unsubscribe=One-Click", message.Headers["List-Unsubscribe-Post"]);
        Assert.Single(message.To);
    }

    [Fact]
    public void Message_WithoutALink_HasNoUnsubscribeHeaders()
    {
        var message = MimeMessageFactory.Create(Settings, Mail(null));

        Assert.Null(message.Headers["List-Unsubscribe"]);
        Assert.Null(message.Headers["List-Unsubscribe-Post"]);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:a@example.com")]
    [InlineData("/relative/path")]
    [InlineData("https://x.example/a b")]
    [InlineData("https://x.example/a>\r\nBcc: v@example.com")]
    [InlineData("https://x.example/a>, <https://evil.example/")]
    [InlineData("https://x.example/\"quoted")]
    public void UnsafeLink_IsRefused_NeverPutInAHeader(string url)
    {
        Assert.Throws<ArgumentException>(() => MimeMessageFactory.Create(Settings, Mail(url)));
    }

    // Footer links in the rendered digest

    private static RenderedDigest Render(DigestRecipient recipient)
    {
        var item = new DiscoveredMediaItem { ItemId = Guid.NewGuid(), ItemType = DiscoveredMediaItemType.Movie, Name = "Heat", Category = MediaCategory.Movies };
        return new DigestRenderer(new LocalizationService()).Render(new DigestBuilder().Build(new[] { item }, DigestOptions.Default), recipient);
    }

    [Theory]
    [InlineData("en", "Unsubscribe from these emails")]
    [InlineData("es", "Darse de baja de estos correos")]
    [InlineData("gl", "Darse de baixa destes correos")]
    public void Footer_ShowsBothLinks_InTheRecipientsLanguage(string language, string unsubscribeLabel)
    {
        var rendered = Render(new DigestRecipient("Ana", language, "https://x.example/EmailDigest/Unsubscribe?token=t", "https://x.example/EmailDigest/Me/Page"));

        Assert.Contains(unsubscribeLabel + ": https://x.example/EmailDigest/Unsubscribe?token=t", rendered.TextBody, StringComparison.Ordinal);
        Assert.Contains("https://x.example/EmailDigest/Me/Page", rendered.TextBody, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://x.example/EmailDigest/Unsubscribe?token=t\"", rendered.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(unsubscribeLabel + "</a>", rendered.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Footer_WithoutLinks_IsUnchanged()
    {
        var rendered = Render(new DigestRecipient("Ana", "en"));

        Assert.DoesNotContain("Unsubscribe", rendered.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Unsubscribe", rendered.HtmlBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://x.example/\"onclick=\"x")]
    [InlineData("https://x.example/<script>")]
    [InlineData("https://x.example/a b")]
    [InlineData("ftp://x.example/")]
    [InlineData("not a url")]
    public void Footer_IgnoresALinkThatIsNotAPlainHttpAddress(string url)
    {
        var rendered = Render(new DigestRecipient("Ana", "en", url, url));

        Assert.DoesNotContain("Unsubscribe from these emails", rendered.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(url, rendered.HtmlBody, StringComparison.Ordinal);
    }
}
