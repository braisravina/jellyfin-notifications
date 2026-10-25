using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Services;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Services;

public sealed class SmtpPasswordStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "emaildigest-pw-" + Guid.NewGuid().ToString("N"));
    private string? _external;

    public SmtpPasswordStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private SmtpPasswordStore CreateStore()
    {
        return new SmtpPasswordStore(_directory, () => _external);
    }

    [Fact]
    public void GetStatus_Fresh_IsNotSet()
    {
        using var store = CreateStore();

        Assert.Equal(new SmtpPasswordStatus(false, SmtpPasswordSource.None), store.GetStatus());
    }

    [Fact]
    public async Task SetAsync_Creates_AndStatusAndReadReflectIt()
    {
        using var store = CreateStore();

        await store.SetAsync("s3cret pass", CancellationToken.None);

        Assert.Equal(new SmtpPasswordStatus(true, SmtpPasswordSource.Stored), store.GetStatus());
        Assert.Equal("s3cret pass", await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SetAsync_Replaces()
    {
        using var store = CreateStore();
        await store.SetAsync("first", CancellationToken.None);

        await store.SetAsync("second", CancellationToken.None);

        Assert.Equal("second", await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_WithoutChange_PreservesTheExistingPassword()
    {
        using var store = CreateStore();
        await store.SetAsync("keep-me", CancellationToken.None);

        // "Leave unchanged" is simply not calling SetAsync; a fresh store instance still sees it.
        using var reopened = CreateStore();

        Assert.Equal("keep-me", await reopened.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ClearAsync_RemovesIt()
    {
        using var store = CreateStore();
        await store.SetAsync("gone", CancellationToken.None);

        await store.ClearAsync(CancellationToken.None);

        Assert.Equal(new SmtpPasswordStatus(false, SmtpPasswordSource.None), store.GetStatus());
        Assert.Null(await store.ReadAsync(CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task ClearAsync_WhenNothingStored_IsHarmless()
    {
        using var store = CreateStore();

        await store.ClearAsync(CancellationToken.None);

        Assert.False(store.GetStatus().IsSet);
    }

    [Theory]
    [InlineData("")]
    [InlineData("line1\nline2")]
    [InlineData("tab\there")]
    [InlineData("nul\0byte")]
    public async Task SetAsync_InvalidValue_IsRefusedAndExistingPasswordKept(string bad)
    {
        using var store = CreateStore();
        await store.SetAsync("good", CancellationToken.None);

        var ex = await Assert.ThrowsAsync<SmtpPasswordException>(() => store.SetAsync(bad, CancellationToken.None));

        Assert.Equal(SmtpPasswordFailure.Invalid, ex.Failure);
        Assert.Equal("good", await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SetAsync_TooLong_IsRefused()
    {
        using var store = CreateStore();

        await Assert.ThrowsAsync<SmtpPasswordException>(() => store.SetAsync(new string('x', 513), CancellationToken.None));
    }

    [Fact]
    public async Task SetAsync_UnicodeAndSpaces_RoundTripExactly()
    {
        using var store = CreateStore();
        const string Password = "  pässwörd 密码 🔑  ";

        await store.SetAsync(Password, CancellationToken.None);

        Assert.Equal(Password, await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SetAsync_Failure_NeverIncludesThePasswordInTheMessage()
    {
        using var store = CreateStore();

        var ex = await Assert.ThrowsAsync<SmtpPasswordException>(() => store.SetAsync("bad\nSECRETVALUE", CancellationToken.None));

        Assert.DoesNotContain("SECRETVALUE", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetAsync_CancelledToken_ThrowsAndWritesNothing()
    {
        using var store = CreateStore();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SetAsync("x", cts.Token));

        Assert.False(store.GetStatus().IsSet);
    }

    [Fact]
    public async Task SetAsync_OnUnix_CreatesOwnerOnlyFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var store = CreateStore();
        await store.SetAsync("perm", CancellationToken.None);

        var file = Directory.GetFiles(_directory)[0];
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }

    [Fact]
    public async Task SetAsync_ReplacingAnExistingWorldReadableTemp_StillOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var temp = Path.Combine(_directory, "smtp-password.secret.tmp");
        await File.WriteAllTextAsync(temp, "stale");
        File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        using var store = CreateStore();

        await store.SetAsync("fresh", CancellationToken.None);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_directory, "smtp-password.secret")));
    }

    // External (Docker secret) source

    [Fact]
    public async Task External_FileWithTrailingNewline_IsUsedWithNewlineRemoved()
    {
        var secret = Path.Combine(_directory, "docker-secret");
        await File.WriteAllTextAsync(secret, "from-docker\n");
        _external = secret;
        using var store = CreateStore();

        Assert.Equal(new SmtpPasswordStatus(true, SmtpPasswordSource.External), store.GetStatus());
        Assert.Equal("from-docker", await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task External_TakesPrecedenceOverStored()
    {
        using var store = CreateStore();
        await store.SetAsync("stored", CancellationToken.None);
        var secret = Path.Combine(_directory, "docker-secret");
        await File.WriteAllTextAsync(secret, "external");
        _external = secret;

        Assert.Equal("external", await store.ReadAsync(CancellationToken.None));
        Assert.Equal(SmtpPasswordSource.External, store.GetStatus().Source);
    }

    [Fact]
    public async Task External_SetAndClear_AreRefused()
    {
        _external = Path.Combine(_directory, "docker-secret");
        using var store = CreateStore();

        var set = await Assert.ThrowsAsync<SmtpPasswordException>(() => store.SetAsync("x", CancellationToken.None));
        var clear = await Assert.ThrowsAsync<SmtpPasswordException>(() => store.ClearAsync(CancellationToken.None));

        Assert.Equal(SmtpPasswordFailure.ManagedExternally, set.Failure);
        Assert.Equal(SmtpPasswordFailure.ManagedExternally, clear.Failure);
    }

    [Fact]
    public void External_MissingOrEmptyFile_ReportsSourceButNotSet()
    {
        _external = Path.Combine(_directory, "missing");
        using var store = CreateStore();

        Assert.Equal(new SmtpPasswordStatus(false, SmtpPasswordSource.External), store.GetStatus());
    }

    [Fact]
    public async Task External_EmptyFile_ReadsAsNull()
    {
        var secret = Path.Combine(_directory, "docker-secret");
        await File.WriteAllTextAsync(secret, "\n");
        _external = secret;
        using var store = CreateStore();

        Assert.Null(await store.ReadAsync(CancellationToken.None));
        Assert.False(store.GetStatus().IsSet);
    }
}
