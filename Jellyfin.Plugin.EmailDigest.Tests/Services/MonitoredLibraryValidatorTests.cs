using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Services;

public sealed class MonitoredLibraryValidatorTests
{
    private static List<MonitoredLibrary> Libraries(params Guid[] ids)
    {
        return ids.Select(id => new MonitoredLibrary { LibraryId = id, Category = MediaCategory.Movies }).ToList();
    }

    private static (Mock<ILibraryManager> LibraryManager, MonitoredLibraryValidator Validator) Create(params string[] virtualFolderIds)
    {
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager
            .Setup(m => m.GetVirtualFolders())
            .Returns(virtualFolderIds.Select(id => new VirtualFolderInfo { ItemId = id, Name = "lib-" + id }).ToList());
        return (libraryManager, new MonitoredLibraryValidator(libraryManager.Object));
    }

    [Fact]
    public void Validate_ExistingLibraries_NoIssues()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var (_, validator) = Create(a.ToString("N"), b.ToString("D"));

        Assert.Empty(validator.Validate(Libraries(a, b)));
    }

    [Fact]
    public void Validate_EmptyConfiguration_NoIssuesAndJellyfinNotQueried()
    {
        var (libraryManager, validator) = Create(Guid.NewGuid().ToString("N"));

        Assert.Empty(validator.Validate(Libraries()));
        libraryManager.Verify(m => m.GetVirtualFolders(), Times.Never);
    }

    [Fact]
    public void Validate_EmptyGuid_ReportedAsInvalidId()
    {
        var (_, validator) = Create(Guid.NewGuid().ToString("N"));

        var issue = Assert.Single(validator.Validate(Libraries(Guid.Empty)));

        Assert.Equal(new MonitoredLibraryIssue(0, Guid.Empty, MonitoredLibraryIssueKind.InvalidId), issue);
    }

    [Fact]
    public void Validate_DeletedLibrary_ReportedAsNotFound()
    {
        var kept = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var (_, validator) = Create(kept.ToString("N"));

        var issue = Assert.Single(validator.Validate(Libraries(kept, deleted)));

        Assert.Equal(new MonitoredLibraryIssue(1, deleted, MonitoredLibraryIssueKind.NotFound), issue);
    }

    [Fact]
    public void Validate_DuplicateLibrary_SecondEntryReportedAsDuplicate()
    {
        var a = Guid.NewGuid();
        var (_, validator) = Create(a.ToString("N"));

        var issue = Assert.Single(validator.Validate(Libraries(a, a)));

        Assert.Equal(new MonitoredLibraryIssue(1, a, MonitoredLibraryIssueKind.Duplicate), issue);
    }

    [Fact]
    public void Validate_JellyfinHasNoLibraries_EveryEntryNotFound()
    {
        var (_, validator) = Create();

        var issues = validator.Validate(Libraries(Guid.NewGuid(), Guid.NewGuid()));

        Assert.All(issues, i => Assert.Equal(MonitoredLibraryIssueKind.NotFound, i.Kind));
        Assert.Equal(2, issues.Count);
    }

    [Fact]
    public void Validate_VirtualFolderWithMalformedOrMissingId_IgnoredNotThrown()
    {
        var a = Guid.NewGuid();
        var (_, validator) = Create("not-a-guid", string.Empty, Guid.Empty.ToString("N"), a.ToString("N"));

        Assert.Empty(validator.Validate(Libraries(a)));
        Assert.Single(validator.Validate(Libraries(Guid.NewGuid())));
    }

    [Fact]
    public void Validate_JellyfinFailure_Propagates()
    {
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(m => m.GetVirtualFolders()).Throws(new InvalidOperationException("boom"));
        var validator = new MonitoredLibraryValidator(libraryManager.Object);

        // A failure must not be reported as "all libraries deleted".
        Assert.Throws<InvalidOperationException>(() => validator.Validate(Libraries(Guid.NewGuid())));
    }

    [Fact]
    public void Validate_MultipleProblems_ReportedInConfigurationOrder()
    {
        var good = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var (_, validator) = Create(good.ToString("N"));

        var issues = validator.Validate(Libraries(Guid.Empty, good, deleted, good));

        Assert.Equal(
            new[]
            {
                new MonitoredLibraryIssue(0, Guid.Empty, MonitoredLibraryIssueKind.InvalidId),
                new MonitoredLibraryIssue(2, deleted, MonitoredLibraryIssueKind.NotFound),
                new MonitoredLibraryIssue(3, good, MonitoredLibraryIssueKind.Duplicate)
            },
            issues);
    }

    [Fact]
    public void Validate_NullConfiguration_Throws()
    {
        var (_, validator) = Create();

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
    }
}
