using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Services;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Services;

public sealed class CheckpointServiceTests : IDisposable
{
    private readonly string _dataFolder;
    private readonly CheckpointService _service;

    public CheckpointServiceTests()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), "EmailDigestTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_dataFolder);
        _service = new CheckpointService(_dataFolder);
    }

    [Fact]
    public async Task GetLastSuccessfulRunAsync_NoCheckpointFile_ReturnsNull()
    {
        var result = await _service.GetLastSuccessfulRunAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveSuccessfulRunAsync_ThenGet_RoundTrips()
    {
        var checkpoint = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

        await _service.SaveSuccessfulRunAsync(checkpoint, CancellationToken.None);
        var result = await _service.GetLastSuccessfulRunAsync(CancellationToken.None);

        Assert.Equal(checkpoint, result);
    }

    [Fact]
    public async Task SaveSuccessfulRunAsync_CalledTwice_OverwritesPreviousValue()
    {
        var first = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

        await _service.SaveSuccessfulRunAsync(first, CancellationToken.None);
        await _service.SaveSuccessfulRunAsync(second, CancellationToken.None);
        var result = await _service.GetLastSuccessfulRunAsync(CancellationToken.None);

        Assert.Equal(second, result);
    }

    [Fact]
    public async Task SaveSuccessfulRunAsync_LeavesNoTempFileBehind()
    {
        await _service.SaveSuccessfulRunAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.DoesNotContain(Directory.GetFiles(_dataFolder), f => f.EndsWith(".tmp", StringComparison.Ordinal));
        Assert.Contains(Directory.GetFiles(_dataFolder), f => f.EndsWith("checkpoint.json", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _service.Dispose();
        Directory.Delete(_dataFolder, recursive: true);
    }
}
