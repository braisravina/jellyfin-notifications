using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.EmailDigest.Admin;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Admin;

/// <summary>
/// Static checks of the embedded administrator page. They cannot replace trying the page in a real
/// Jellyfin dashboard (see the live-verification document), but they pin the properties that matter
/// for secrecy and that would silently break the page if the API or resources changed.
/// </summary>
public sealed class ConfigPageTests
{
    private static readonly string Html = LoadPage();

    private static string LoadPage()
    {
        var assembly = typeof(Plugin).Assembly;
        using var stream = assembly.GetManifestResourceStream(typeof(Plugin).Namespace + ".Configuration.configPage.html");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    private static IEnumerable<string> Matches(string pattern)
    {
        return Regex.Matches(Html, pattern).Select(m => m.Groups[1].Value);
    }

    [Fact]
    public void PasswordInput_IsMaskedWriteOnlyAndNeverPrefilled()
    {
        var passwordInputs = Regex.Matches(Html, "<input[^>]*type=\"password\"[^>]*>").Select(m => m.Value).ToList();

        var input = Assert.Single(passwordInputs);
        Assert.Contains("id=\"NewPassword\"", input, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"new-password\"", input, StringComparison.Ordinal);
        Assert.DoesNotContain("value=", input, StringComparison.Ordinal);
        Assert.Contains("spellcheck=\"false\"", input, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_NeverReadsAPasswordFromTheServerIntoTheForm()
    {
        Assert.DoesNotContain("SmtpPassword", Html, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"NewPassword'\)\.value\s*=[^=]", Html);
        Assert.DoesNotMatch(@"Password\s*:\s*[a-z]+\.Password", Html);
    }

    [Fact]
    public void Page_ClearsThePasswordFieldBeforeSendingAndSendsItOnlyInABody()
    {
        Assert.Contains("field.value = '';", Html, StringComparison.Ordinal);
        Assert.Contains("api('PUT', 'Password', { Password: value })", Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Password?", Html, StringComparison.Ordinal);
        Assert.DoesNotContain("+ value", Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("innerHTML")]
    [InlineData("outerHTML")]
    [InlineData("insertAdjacentHTML")]
    [InlineData("document.write")]
    [InlineData("eval(")]
    [InlineData("new Function")]
    [InlineData("setTimeout('")]
    public void Page_NeverBuildsMarkupOrCodeFromData(string forbidden)
    {
        Assert.DoesNotContain(forbidden, Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("localStorage")]
    [InlineData("sessionStorage")]
    [InlineData("indexedDB")]
    [InlineData("document.cookie")]
    [InlineData("console.")]
    [InlineData("window.name")]
    [InlineData("history.")]
    [InlineData("location.hash")]
    public void Page_StoresAndLogsNothingInTheBrowser(string forbidden)
    {
        Assert.DoesNotContain(forbidden, Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_LoadsNothingFromOtherOrigins()
    {
        Assert.DoesNotContain("http://", Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script src", Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Page_UsesTextContentForServerText()
    {
        Assert.Contains("element.textContent = text", Html, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryI18nKeyAndEveryTranslatedLookupIsAServedAdminString()
    {
        var served = new HashSet<string>(AdminStringKeys.All, StringComparer.Ordinal);
        var keys = Matches("data-i18n=\"([A-Za-z]+)\"").Concat(Matches(@"\bt\('([A-Za-z]+)'\)")).Distinct().ToList();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.Contains(key, served));
    }

    [Fact]
    public void PasswordStatusKeysCoverEverySource()
    {
        foreach (var source in new[] { "None", "Stored", "External" })
        {
            Assert.Contains("AdminPasswordStatus" + source, AdminStringKeys.All);
        }
    }

    [Fact]
    public void EverySelectorTheScriptUsesExistsInTheMarkup()
    {
        var ids = new HashSet<string>(Matches("\\bid=\"([A-Za-z]+)\""), StringComparer.Ordinal);
        var used = Matches(@"\$\('#([A-Za-z]+)'\)").Concat(Matches(@"'#([A-Za-z]+)'")).Distinct().ToList();

        Assert.NotEmpty(used);
        Assert.All(used, id => Assert.Contains(id, ids));
    }

    [Fact]
    public void EveryApiCallTheScriptMakesMatchesAControllerRoute()
    {
        var routes = typeof(EmailDigestAdminController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.GetCustomAttribute<HttpMethodAttribute>()!)
            .SelectMany(a => a.HttpMethods.Select(method => (Method: method, Path: a.Template!)))
            .ToHashSet();

        var calls = Regex.Matches(Html, @"api\('(GET|PUT|POST|DELETE)', '([A-Za-z]+)")
            .Select(m => (Method: m.Groups[1].Value, Path: m.Groups[2].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.Contains(call, routes));
    }

    [Fact]
    public void DigestSave_RereadsTheConfigurationSoSmtpSettingsAreNotOverwrittenByAStaleCopy()
    {
        var submit = Html[Html.IndexOf("$('#DigestForm').addEventListener", StringComparison.Ordinal)..];
        var firstCall = submit.IndexOf("getPluginConfiguration", StringComparison.Ordinal);
        var save = submit.IndexOf("updatePluginConfiguration", StringComparison.Ordinal);

        Assert.True(firstCall > 0 && firstCall < save);
    }

    [Fact]
    public void UnencryptedChoice_ShowsAWarningAndDisablesTheUsername()
    {
        Assert.Contains("id=\"UnencryptedWarning\"", Html, StringComparison.Ordinal);
        Assert.Contains("$('#UnencryptedWarning').hidden = !unencrypted", Html, StringComparison.Ordinal);
        Assert.Contains("$('#SmtpUsername').disabled = unencrypted", Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Tests_AreDisabledWhileSettingsAreUnsaved()
    {
        Assert.Contains("$('#TestConnectionButton').disabled = value", Html, StringComparison.Ordinal);
        Assert.Contains("$('#TestSendButton').disabled = value", Html, StringComparison.Ordinal);
    }

    [Fact]
    public void TestEmail_NeedsAnExplicitRecipientTheAdministratorTypes()
    {
        Assert.Contains("id=\"TestRecipient\"", Html, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"TestRecipient'\)\.value\s*=[^=]", Html);
    }

    [Fact]
    public void Page_PreservesTheAccessibilityBasics()
    {
        Assert.Contains("aria-live=\"polite\"", Html, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", Html, StringComparison.Ordinal);
        Assert.All(Matches("<input id=\"([A-Za-z]+)\"").Where(id => id != "DigestEnabled" && id != "SmtpEnabled" && id != "DigestDryRun"), id => Assert.Contains($"for=\"{id}\"", Html, StringComparison.Ordinal));
    }
}
