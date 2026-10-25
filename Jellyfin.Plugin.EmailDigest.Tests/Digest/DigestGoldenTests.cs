using System;
using System.IO;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Digest;

/// <summary>
/// Compares full rendered messages against reviewed files in <c>Golden/</c>. To regenerate after an
/// intentional wording or layout change, run the tests with <c>UPDATE_GOLDEN=1</c>, then review the
/// diff of the files before committing.
/// </summary>
public sealed class DigestGoldenTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("gl")]
    public void Html_MatchesGoldenFile(string language)
    {
        AssertGolden(language + ".html", DigestRendererTests.RenderGolden(language).HtmlBody);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("gl")]
    public void Text_MatchesGoldenFile(string language)
    {
        AssertGolden(language + ".txt", DigestRendererTests.RenderGolden(language).TextBody);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("gl")]
    public void Subject_MatchesGoldenFile(string language)
    {
        AssertGolden(language + ".subject.txt", DigestRendererTests.RenderGolden(language).Subject + "\n");
    }

    private static void AssertGolden(string fileName, string actual)
    {
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(Path.Combine(SourceGoldenDirectory(), fileName), actual);
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Golden", fileName);
        Assert.True(File.Exists(path), "Missing golden file " + fileName + "; run with UPDATE_GOLDEN=1 to create it.");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), actual);
    }

    private static string SourceGoldenDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Jellyfin.Plugin.EmailDigest.Tests.csproj")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Test project directory not found."), "Golden");
    }
}
