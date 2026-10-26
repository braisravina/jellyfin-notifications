using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Jellyfin.Plugin.EmailDigest.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Storage;

public sealed class StorageMigratorTests : IDisposable
{
    private static readonly string[] StateFiles =
    [
        "user-preferences.json", "delivery-state.json", "delivery-state.json.bak",
        "checkpoint.json", "smtp-password.secret", "last-run.json"
    ];

    private readonly string _work = Path.Combine(Path.GetTempPath(), "EmailDigestStorage_" + Guid.NewGuid());
    private readonly string _data;
    private readonly string _legacy;

    public StorageMigratorTests()
    {
        _data = Path.Combine(_work, "data");
        _legacy = Path.Combine(_work, "plugins", "Jellyfin.Plugin.EmailDigest");
        Directory.CreateDirectory(_data);
    }

    private string Root => Path.Combine(_data, StorageMigrator.FolderName);

    public void Dispose()
    {
        Directory.Delete(_work, recursive: true);
    }

    [Fact]
    public void FreshInstall_CreatesRootOutsideThePluginsTree_AndMarker()
    {
        var storage = Prepare();

        Assert.Equal(Root, storage.Root);
        Assert.True(Directory.Exists(Root));
        Assert.True(File.Exists(Path.Combine(Root, StorageMigrator.MarkerName)));
        Assert.False(storage.Root.StartsWith(Path.GetDirectoryName(_legacy)!, StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyFiles_AreCopiedByteForByte_AndLeftUntouched()
    {
        Seed(_legacy);
        var before = Snapshot(_legacy);

        Prepare();

        foreach (var name in StateFiles)
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(_legacy, name)), File.ReadAllBytes(Path.Combine(Root, name)));
        }

        Assert.Equal(before, Snapshot(_legacy));
    }

    [Fact]
    public void SecondRun_ChangesNothing_AndDoesNotRecopy()
    {
        Seed(_legacy);
        Prepare();
        File.WriteAllText(Path.Combine(Root, "user-preferences.json"), "newer");

        Prepare();

        Assert.Equal("newer", File.ReadAllText(Path.Combine(Root, "user-preferences.json")));
    }

    [Fact]
    public void ExistingDestinationFile_IsNeverOverwritten()
    {
        Seed(_legacy);
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "delivery-state.json"), "newer state");

        Prepare();

        Assert.Equal("newer state", File.ReadAllText(Path.Combine(Root, "delivery-state.json")));
        Assert.Equal("legacy delivery-state.json", File.ReadAllText(Path.Combine(_legacy, "delivery-state.json")));
        Assert.Equal("legacy user-preferences.json", File.ReadAllText(Path.Combine(Root, "user-preferences.json")));
    }

    [Fact]
    public void InterruptedMigration_LeftoverTemporaryFileIsDiscarded_AndMigrationCompletes()
    {
        Seed(_legacy);
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "user-preferences.json.migrating"), "half writ");
        File.WriteAllText(Path.Combine(Root, "delivery-state.json"), "legacy delivery-state.json");

        Prepare();

        Assert.False(File.Exists(Path.Combine(Root, "user-preferences.json.migrating")));
        Assert.Equal("legacy user-preferences.json", File.ReadAllText(Path.Combine(Root, "user-preferences.json")));
        Assert.True(File.Exists(Path.Combine(Root, StorageMigrator.MarkerName)));
    }

    [Fact]
    public void DamagedMarker_IsTreatedAsIncomplete_AndRewritten()
    {
        Seed(_legacy);
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, StorageMigrator.MarkerName), "{ not json");

        Prepare();

        Assert.Equal("legacy checkpoint.json", File.ReadAllText(Path.Combine(Root, "checkpoint.json")));
        Assert.Contains("\"Format\": 1", File.ReadAllText(Path.Combine(Root, StorageMigrator.MarkerName)), StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeOnlyFiles_AreNotMigrated()
    {
        Seed(_legacy);
        File.WriteAllText(Path.Combine(_legacy, "digest-run.lock"), "lock");

        Prepare();

        Assert.False(File.Exists(Path.Combine(Root, "digest-run.lock")));
    }

    [Fact]
    public void UnrelatedFilesInTheLegacyFolder_AreNotCopied()
    {
        Seed(_legacy);
        File.WriteAllText(Path.Combine(_legacy, "other-plugin-note.txt"), "x");

        Prepare();

        Assert.False(File.Exists(Path.Combine(Root, "other-plugin-note.txt")));
    }

    [Fact]
    public void LegacyFolderSameAsStableFolder_CopiesNothingAndDeletesNothing()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "user-preferences.json"), "keep");

        StorageMigrator.Prepare(_data, Root, TimeProvider.System, NullLogger.Instance);

        Assert.Equal("keep", File.ReadAllText(Path.Combine(Root, "user-preferences.json")));
    }

    [Fact]
    public void FailureToCopy_ThrowsFailClosed_AndLeavesLegacyIntact()
    {
        Seed(_legacy);
        var before = Snapshot(_legacy);
        // A directory squatting on a destination file name makes the copy impossible.
        Directory.CreateDirectory(Path.Combine(Root, "user-preferences.json"));

        Assert.Throws<StorageMigrationException>(Prepare);
        Assert.Equal(before, Snapshot(_legacy));
        Assert.False(File.Exists(Path.Combine(Root, StorageMigrator.MarkerName)));
    }

    [Fact]
    public void ErrorMessage_NeverContainsFileContent()
    {
        Seed(_legacy);
        File.WriteAllText(Path.Combine(_legacy, "smtp-password.secret"), "hunter2-secret");
        Directory.CreateDirectory(Path.Combine(Root, "smtp-password.secret"));

        var ex = Assert.Throws<StorageMigrationException>(Prepare);

        Assert.DoesNotContain("hunter2", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NewFilesAndFolders_AreOwnerOnly_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Seed(_legacy);
        Prepare();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Root));
        foreach (var name in StateFiles.Append(StorageMigrator.MarkerName))
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(Root, name)));
        }
    }

    [Fact]
    public void Downgrade_LegacyFolderStillHoldsTheStateItHadAtUpgrade()
    {
        Seed(_legacy);
        Prepare();
        File.WriteAllText(Path.Combine(Root, "delivery-state.json"), "advanced after upgrade");

        // An older version reads the legacy folder only: it still has its own complete files.
        Assert.Equal("legacy delivery-state.json", File.ReadAllText(Path.Combine(_legacy, "delivery-state.json")));
        Assert.Equal(StateFiles.Length, StateFiles.Count(name => File.Exists(Path.Combine(_legacy, name))));
    }

    [Fact]
    public void ReUpgradeAfterDowngrade_KeepsTheStableFolderAndDoesNotImportOlderLegacyState()
    {
        Seed(_legacy);
        Prepare();
        File.WriteAllText(Path.Combine(Root, "delivery-state.json"), "advanced after upgrade");
        File.WriteAllText(Path.Combine(_legacy, "delivery-state.json"), "changed while downgraded");
        File.Delete(Path.Combine(Root, StorageMigrator.MarkerName));

        Prepare();

        Assert.Equal("advanced after upgrade", File.ReadAllText(Path.Combine(Root, "delivery-state.json")));
    }

    private PluginStorage Prepare()
    {
        return StorageMigrator.Prepare(_data, _legacy, TimeProvider.System, NullLogger.Instance);
    }

    private static void Seed(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var name in StateFiles)
        {
            File.WriteAllText(Path.Combine(folder, name), "legacy " + name);
        }
    }

    private static string Snapshot(string folder)
    {
        return string.Join(
            "|",
            Directory.GetFiles(folder).OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => Path.GetFileName(f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))));
    }
}
