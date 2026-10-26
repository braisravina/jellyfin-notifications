using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Services;
using Jellyfin.Plugin.EmailDigest.Subscription;
using Jellyfin.Plugin.EmailDigest.UserApi;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.UserApi;

public sealed partial class PageTests : IAsyncDisposable
{
    private readonly Mock<ISubscriptionService> _service = new();
    private readonly PipelineHost _host;
    private readonly HttpClient _client;
    private readonly LocalizationService _text = new();

    public PageTests()
    {
        _host = new PipelineHost(_service.Object);
        _client = _host.Client();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
    }

    [GeneratedRegex(@"<script>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex ScriptBlock();

    [GeneratedRegex(@"\{\d\}")]
    private static partial Regex Placeholder();

    private static string Script => ScriptBlock().Match(PageContent.Source()).Groups[1].Value;

    // Strings

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("gl")]
    public void EveryPageKey_ExistsInEveryLanguage_WithTheSamePlaceholders(string language)
    {
        foreach (var key in UserStringKeys.All)
        {
            var value = _text.GetString(language, key);
            Assert.NotEqual(key, value);
            Assert.False(string.IsNullOrWhiteSpace(value), key);
            Assert.Equal(
                Placeholder().Matches(_text.GetString("en", key)).Select(m => m.Value).OrderBy(x => x).ToArray(),
                Placeholder().Matches(value).Select(m => m.Value).OrderBy(x => x).ToArray());
        }
    }

    [Fact]
    public void ThePageKeys_AreUnique_AndTheSpanishAndGalicianAreReallyTranslated()
    {
        Assert.Equal(UserStringKeys.All.Count, UserStringKeys.All.Distinct().Count());
        var identical = UserStringKeys.All.Count(k => _text.GetString("es", k) == _text.GetString("en", k));
        Assert.True(identical <= 2, "Spanish strings identical to English: " + identical);
        Assert.True(UserStringKeys.All.Count(k => _text.GetString("gl", k) == _text.GetString("es", k)) < UserStringKeys.All.Count / 3);
    }

    [Fact]
    public void TheKeysTheScriptUses_AreAllInTheList()
    {
        var used = Regex.Matches(Script + PageContent.Source(), @"\bt\('([A-Za-z]+)'\)").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(Script, @"'((?:User|Link)[A-Za-z]+)'").Select(m => m.Groups[1].Value))
            .Distinct()
            .Where(k => k.StartsWith("User", StringComparison.Ordinal) || k.StartsWith("Link", StringComparison.Ordinal))
            .Where(k => k is not ("LinkVerify" or "LinkUnsubscribe")) // prefixes: the suffixes are checked below
            .ToList();

        Assert.NotEmpty(used);
        Assert.All(used, k => Assert.Contains(k, UserStringKeys.All));
        foreach (var prefix in new[] { "LinkVerify", "LinkUnsubscribe" })
        {
            foreach (var suffix in new[] { "Title", "Text", "Button", "Done", "Invalid" })
            {
                Assert.Contains(prefix + suffix, UserStringKeys.All);
            }
        }
    }

    [Fact]
    public async Task StringsEndpoint_NeedsNoSignIn_ReturnsExactlyTheListedKeys_AndFallsBackToEnglish()
    {
        var response = await _client.GetAsync("/EmailDigest/Strings?language=gl");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("gl", document.RootElement.GetProperty("language").GetString());
        var keys = document.RootElement.GetProperty("strings").EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(UserStringKeys.All.OrderBy(k => k, StringComparer.Ordinal).ToArray(), keys);

        using var fallback = JsonDocument.Parse(await (await _client.GetAsync("/EmailDigest/Strings?language=fr")).Content.ReadAsStringAsync());
        Assert.Equal("en", fallback.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task StringsEndpoint_NeverReturnsAdministratorOrDigestText()
    {
        var body = await (await _client.GetAsync("/EmailDigest/Strings?language=en")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("AdminTitle", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SectionMovies", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SMTP", body, StringComparison.Ordinal);
    }

    // The pages

    [Theory]
    [InlineData("/EmailDigest/Me/Page", "me")]
    [InlineData("/EmailDigest/Verify", "verify")]
    [InlineData("/EmailDigest/Unsubscribe", "unsubscribe")]
    public async Task EachPage_IsServedWithoutSignIn_ChangesNothing_AndIsLockedDown(string path, string mode)
    {
        var response = await _client.GetAsync(path + (mode == "me" ? string.Empty : "?token=abc"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-mode=\"" + mode + "\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ThePolicy_AllowsOnlyThePagesOwnScriptAndStyle_ByHash_AndTheHashesMatchTheServedPage()
    {
        var response = await _client.GetAsync("/EmailDigest/Me/Page");
        var html = await response.Content.ReadAsStringAsync();
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();

        var scriptHash = "sha256-" + Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ScriptBlock().Match(html).Groups[1].Value)));
        var styleHash = "sha256-" + Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Regex.Match(html, "<style>(.*?)</style>", RegexOptions.Singleline).Groups[1].Value)));
        Assert.Contains("default-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("script-src '" + scriptHash + "'", policy, StringComparison.Ordinal);
        Assert.Contains("style-src '" + styleHash + "'", policy, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("*", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllThreePages_ShareOneScript_SoOneHashCoversThem()
    {
        var scripts = await Task.WhenAll(new[] { "/EmailDigest/Me/Page", "/EmailDigest/Verify", "/EmailDigest/Unsubscribe" }
            .Select(async p => ScriptBlock().Match(await (await _client.GetAsync(p)).Content.ReadAsStringAsync()).Groups[1].Value));

        Assert.Single(scripts.Distinct());
    }

    [Fact]
    public void TheScript_NeverReCasesTheStringMapsKeys()
    {
        // norm() lower-cases the first letter of every key; applied to the strings map it would turn
        // "UserTitle" into "userTitle" and every label would show its key. Found by running the page.
        Assert.DoesNotContain("norm(j).strings", Script, StringComparison.Ordinal);
        Assert.Matches(@"strings = \(j && \(j\.strings \|\| j\.Strings\)\)", Script);
    }

    [Fact]
    public void TheModeIsAFixedWord_NotAnythingARequestCanChoose()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PageContent.Html("<script>"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PageContent.Html("admin"));
    }

    [Fact]
    public async Task APageUrl_WithHostileQueryText_IsNeverEchoedIntoThePage()
    {
        var html = await (await _client.GetAsync("/EmailDigest/Verify?token=%3Cscript%3Ealert(1)%3C/script%3E&x=\"onload=\"")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("alert(1)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("onload=", html, StringComparison.Ordinal);
    }

    // Static rules for the page's own code

    [Theory]
    [InlineData("innerHTML")]
    [InlineData("outerHTML")]
    [InlineData("insertAdjacentHTML")]
    [InlineData("document.write")]
    [InlineData("eval(")]
    [InlineData("new Function")]
    [InlineData("setTimeout('")]
    [InlineData("setAttribute('style'")]
    [InlineData("srcdoc")]
    [InlineData("postMessage")]
    [InlineData("console.")]
    [InlineData("sessionStorage")]
    public void TheScript_NeverUsesAnUnsafeOrLeakyApi(string forbidden)
    {
        Assert.DoesNotContain(forbidden, Script, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePage_HasNoInlineHandlers_NoStyleAttributes_NoExternalAddresses()
    {
        var html = PageContent.Source();

        Assert.DoesNotMatch(@"\son[a-z]+\s*=", html);
        Assert.DoesNotMatch(@"<[a-z][^>]*\sstyle\s*=", html);
        Assert.DoesNotMatch(@"(src|href)\s*=\s*""https?:", html);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Regex.Matches(html, "<script", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void TheScript_ReadsOnlyTheJellyfinSession_FromLocalStorage_AndSendsOnlyTheToken()
    {
        Assert.Single(Regex.Matches(Script, "localStorage"));
        Assert.Contains("getItem('jellyfin_credentials')", Script, StringComparison.Ordinal);
        Assert.DoesNotContain("setItem", Script, StringComparison.Ordinal);
        Assert.DoesNotContain("removeItem", Script, StringComparison.Ordinal);
        Assert.Contains("'MediaBrowser Token=\"'", Script, StringComparison.Ordinal);
        // Sending Client/Device/Version would rename the browser's session in Jellyfin's device list.
        Assert.DoesNotContain("Device=", Script, StringComparison.Ordinal);
        Assert.DoesNotContain("Client=", Script, StringComparison.Ordinal);
        Assert.DoesNotContain("Version=", Script, StringComparison.Ordinal);
        Assert.DoesNotContain("document.cookie", Script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheScript_NeverPutsAnAddressOrTokenInAUrlItBuilds_ExceptTheConfirmationTokenInTheQuery()
    {
        var urls = Regex.Matches(Script, @"base \+ [^;]*").Select(m => m.Value).ToList();

        Assert.DoesNotContain(urls, u => u.Contains("address", StringComparison.OrdinalIgnoreCase) || u.Contains("email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(urls, u => u.Contains("sessionToken", StringComparison.Ordinal) || u.Contains("AccessToken", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheAnonymousPagesAndStrings_CarryNoPersonalOrServerData()
    {
        foreach (var path in new[] { "/EmailDigest/Me/Page", "/EmailDigest/Verify", "/EmailDigest/Unsubscribe", "/EmailDigest/Strings" })
        {
            var body = await (await _client.GetAsync(path)).Content.ReadAsStringAsync();
            Assert.DoesNotContain("@example", body, StringComparison.Ordinal);
            Assert.DoesNotContain("smtp.", body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
