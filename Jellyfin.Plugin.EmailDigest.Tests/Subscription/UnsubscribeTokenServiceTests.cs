using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.EmailDigest.Subscription;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Subscription;

public sealed class UnsubscribeTokenServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EmailDigestUnsub_" + Guid.NewGuid());
    private readonly UnsubscribeTokenService _tokens;

    public UnsubscribeTokenServiceTests()
    {
        _tokens = new UnsubscribeTokenService(_folder);
    }

    private string KeyPath => Path.Combine(_folder, "unsubscribe.key");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Token_RoundTripsToTheSameUser()
    {
        var user = Guid.NewGuid();

        Assert.Equal(user, _tokens.TryRead(_tokens.Create(user)));
    }

    [Fact]
    public void SameUser_AlwaysGetsTheSameToken_SoOldEmailsKeepWorking()
    {
        var user = Guid.NewGuid();
        var first = _tokens.Create(user);

        Assert.Equal(first, _tokens.Create(user));
        Assert.Equal(first, new UnsubscribeTokenService(_folder).Create(user));
    }

    [Fact]
    public void DifferentUsers_GetDifferentTokens()
    {
        Assert.NotEqual(_tokens.Create(Guid.NewGuid()), _tokens.Create(Guid.NewGuid()));
    }

    [Fact]
    public void Token_IsUrlSafe_AndRevealsNoAddressOrName()
    {
        var token = _tokens.Create(Guid.NewGuid());

        Assert.All(token, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'));
        Assert.DoesNotContain("@", token, StringComparison.Ordinal);
        Assert.True(token.Length < 128);
    }

    [Fact]
    public void ForgedToken_ForAnotherUser_IsRejected()
    {
        var victim = Guid.NewGuid();
        var mine = _tokens.Create(Guid.NewGuid());
        var forged = Convert.ToBase64String(victim.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "." + mine.Split('.')[1];

        Assert.Null(_tokens.TryRead(forged));
    }

    [Fact]
    public void AlteredToken_IsRejected()
    {
        var token = _tokens.Create(Guid.NewGuid());
        var altered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        Assert.Null(_tokens.TryRead(altered));
    }

    [Fact]
    public void NonCanonicalSpelling_OfAValidToken_IsRejected()
    {
        var token = _tokens.Create(Guid.NewGuid());
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

        // The last character carries only 4 meaningful bits; every other spelling of those bits is refused.
        var variants = alphabet.Select(c => token[..^1] + c).Where(v => v != token).ToList();

        Assert.All(variants, v => Assert.Null(_tokens.TryRead(v)));
        Assert.NotNull(_tokens.TryRead(token));
    }

    [Fact]
    public void TokenFromAnotherServersKey_IsRejected()
    {
        var other = new UnsubscribeTokenService(Path.Combine(_folder, "other"));
        var user = Guid.NewGuid();

        Assert.Null(_tokens.TryRead(other.Create(user)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("abc")]
    [InlineData("a.b.c")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("not base64 !.also not")]
    public void MalformedToken_IsRejected(string? token)
    {
        Assert.Null(_tokens.TryRead(token));
    }

    [Fact]
    public void VeryLongToken_IsRejectedWithoutWork()
    {
        Assert.Null(_tokens.TryRead(new string('A', 5000)));
    }

    [Fact]
    public void EmptyUserId_IsRefused_BothWays()
    {
        Assert.Throws<ArgumentException>(() => _tokens.Create(Guid.Empty));
        var zeroId = Convert.ToBase64String(Guid.Empty.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Null(_tokens.TryRead(zeroId + "." + new string('A', 43)));
    }

    [Fact]
    public void Key_IsCreatedOnFirstUse_OwnerOnly_AndReused()
    {
        Assert.False(File.Exists(KeyPath));

        _tokens.Create(Guid.NewGuid());

        Assert.Equal(32, new FileInfo(KeyPath).Length);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyPath));
        }

        var before = File.ReadAllBytes(KeyPath);
        new UnsubscribeTokenService(_folder).Create(Guid.NewGuid());
        Assert.Equal(before, File.ReadAllBytes(KeyPath));
    }

    [Fact]
    public void KeyOfTheWrongSize_IsRefused_NotReplaced()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(KeyPath, new byte[5]);

        Assert.Throws<UnsubscribeTokenException>(() => _tokens.Create(Guid.NewGuid()));
        Assert.Equal(5, new FileInfo(KeyPath).Length);
    }

    [Fact]
    public void DeletingTheKey_InvalidatesEveryEarlierToken()
    {
        var user = Guid.NewGuid();
        var token = _tokens.Create(user);
        File.Delete(KeyPath);

        Assert.Null(new UnsubscribeTokenService(_folder).TryRead(token));
    }

    [Fact]
    public void ConcurrentFirstUse_AllAgreeOnOneKey()
    {
        var user = Guid.NewGuid();
        var tokens = Enumerable.Range(0, 16).AsParallel().Select(_ => new UnsubscribeTokenService(_folder).Create(user)).Distinct().ToList();

        Assert.Single(tokens);
    }
}
