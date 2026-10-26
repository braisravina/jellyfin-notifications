using System;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.EmailDigest.Localization;
using Jellyfin.Plugin.EmailDigest.Services;
using Jellyfin.Plugin.EmailDigest.Subscription;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Subscription;

public sealed class VerificationMessageBuilderTests
{
    private const string Link = "https://jellyfin.example.org/EmailDigest/Verify?token=abcDEF_-123";
    private static readonly DateTimeOffset Expires = new(2026, 5, 1, 13, 5, 0, TimeSpan.Zero);

    private readonly LocalizationService _text = new();
    private readonly VerificationMessageBuilder _builder;

    public VerificationMessageBuilderTests()
    {
        _builder = new VerificationMessageBuilder(_text);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("gl")]
    public void EveryKey_ExistsInEveryLanguage_WithTheSamePlaceholders(string language)
    {
        foreach (var key in SubscriptionStringKeys.VerificationEmail)
        {
            var value = _text.GetString(language, key);
            Assert.NotEqual(key, value);
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.Equal(
                Regex.Matches(_text.GetString("en", key), @"\{\d\}").Select(m => m.Value).OrderBy(x => x),
                Regex.Matches(value, @"\{\d\}").Select(m => m.Value).OrderBy(x => x));
        }
    }

    [Theory]
    [InlineData("en", "Confirm your email address")]
    [InlineData("es", "Confirma tu dirección de correo")]
    [InlineData("gl", "Confirma o teu enderezo de correo")]
    public void Subject_IsInTheRecipientsLanguage(string language, string subject)
    {
        Assert.Equal(subject, _builder.Build("a@example.com", language, "Jellyfin", Link, Expires).Subject);
    }

    [Fact]
    public void UnsupportedLanguage_FallsBackToEnglish()
    {
        Assert.Equal("Confirm your email address", _builder.Build("a@example.com", "fr", "Jellyfin", Link, Expires).Subject);
    }

    [Fact]
    public void Message_HasOneRecipient_TheLink_AndTheExpiry_InBothParts()
    {
        var email = _builder.Build("a@example.com", "en", "Home Jellyfin", Link, Expires);

        Assert.Equal("a@example.com", email.RecipientAddress);
        Assert.Contains(Link, email.TextBody, StringComparison.Ordinal);
        Assert.Contains("href=\"" + Link + "\"", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("2026-05-01 13:05 UTC", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("2026-05-01 13:05 UTC", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Home Jellyfin", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_SaysIgnoringItChangesNothing()
    {
        var email = _builder.Build("a@example.com", "en", "Jellyfin", Link, Expires);

        Assert.Contains("ignore this message", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("nothing will change", email.TextBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("gl")]
    public void Message_ContainsNoMediaLibraryOrUserInformation(string language)
    {
        var email = _builder.Build("a@example.com", language, "Jellyfin", Link, Expires);
        var all = (email.Subject + email.TextBody + email.HtmlBody).ToLowerInvariant();

        foreach (var forbidden in new[] { "movie", "series", "episode", "album", "library", "season", "película", "serie", "episodio", "biblioteca", "temporada", "username", "password" })
        {
            Assert.DoesNotContain(forbidden, all.Replace("ningún contido multimedia", string.Empty, StringComparison.Ordinal).Replace("ningún contenido multimedia", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HtmlPart_EncodesTheServerLabel_SoItCannotInjectMarkup()
    {
        var email = _builder.Build("a@example.com", "en", "<script>alert(1)</script>&\"x", Link, Expires);

        Assert.DoesNotContain("<script>", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlPart_EncodesTheLinkInTheAttribute()
    {
        var email = _builder.Build("a@example.com", "en", "Jellyfin", "https://x.example/p?token=a\"onmouseover=\"b", Expires);

        Assert.DoesNotContain("\"onmouseover=", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void BlankServerLabel_UsesAGenericName()
    {
        var email = _builder.Build("a@example.com", "en", "   ", Link, Expires);

        Assert.Contains("notifications from Jellyfin.", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SubjectNeverContainsALineBreak_EvenWithAHostileLabel()
    {
        var email = _builder.Build("a@example.com", "en", "x\r\nBcc: v@example.com", Link, Expires);

        Assert.DoesNotContain('\n', email.Subject);
        Assert.DoesNotContain('\r', email.Subject);
    }
}
