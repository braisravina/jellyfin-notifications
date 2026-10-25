using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.EmailDigest.Admin;
using Jellyfin.Plugin.EmailDigest.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Admin;

public sealed class LibraryAdminServiceTests
{
    private static readonly Guid MoviesId = Guid.NewGuid();
    private static readonly Guid ShowsId = Guid.NewGuid();
    private static readonly Guid MusicId = Guid.NewGuid();

    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly FakeAccessor _accessor = new();

    public LibraryAdminServiceTests()
    {
        SetLibraries(
            Folder(MoviesId, "Movies", CollectionTypeOptions.movies),
            Folder(ShowsId, "Shows & <b>Anime</b>", CollectionTypeOptions.tvshows),
            Folder(MusicId, "Music", CollectionTypeOptions.music));
    }

    private LibraryAdminService Service()
    {
        return new LibraryAdminService(_libraryManager.Object, _accessor, NullLogger<LibraryAdminService>.Instance);
    }

    private static VirtualFolderInfo Folder(Guid id, string name, CollectionTypeOptions? type)
    {
        return new VirtualFolderInfo { ItemId = id.ToString("N"), Name = name, CollectionType = type };
    }

    private void SetLibraries(params VirtualFolderInfo[] folders)
    {
        _libraryManager.Setup(m => m.GetVirtualFolders()).Returns(folders.ToList());
    }

    private static LibrariesUpdate Update(params (Guid Id, string? Category)[] choices)
    {
        return new LibrariesUpdate(choices.Select(c => new LibrarySelection(c.Id, c.Category)).ToList());
    }

    [Fact]
    public void Get_ListsEveryLibraryWithNothingMonitoredByDefault()
    {
        var dto = Assert.IsType<LibrariesDto>(Service().GetLibraries().Body);

        Assert.Equal(3, dto.Libraries.Count);
        Assert.All(dto.Libraries, l => Assert.False(l.Monitored));
        Assert.Empty(dto.Missing);
        Assert.Contains("Anime", dto.Categories);
        Assert.Contains("Documentaries", dto.Categories);
    }

    [Fact]
    public void Get_SuggestsACategoryOnlyForUnambiguousJellyfinTypes_NeverAnime()
    {
        SetLibraries(Folder(MoviesId, "A", CollectionTypeOptions.movies), Folder(ShowsId, "B", CollectionTypeOptions.tvshows), Folder(MusicId, "C", CollectionTypeOptions.music), Folder(Guid.NewGuid(), "Anime", CollectionTypeOptions.homevideos), Folder(Guid.NewGuid(), "D", null));

        var dto = (LibrariesDto)Service().GetLibraries().Body!;

        Assert.Equal("Movies", dto.Libraries.Single(l => l.Id == MoviesId).SuggestedCategory);
        Assert.Equal("Series", dto.Libraries.Single(l => l.Id == ShowsId).SuggestedCategory);
        Assert.Equal("Music", dto.Libraries.Single(l => l.Id == MusicId).SuggestedCategory);
        Assert.Equal(2, dto.Libraries.Count(l => l.SuggestedCategory.Length == 0));
        Assert.DoesNotContain(dto.Libraries, l => l.SuggestedCategory == "Anime");
    }

    [Fact]
    public void Get_ReflectsConfiguredChoicesAndReportsLibrariesJellyfinNoLongerHas()
    {
        var deleted = Guid.NewGuid();
        _accessor.Current.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = ShowsId, Category = MediaCategory.Anime });
        _accessor.Current.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = deleted, Category = MediaCategory.Movies });

        var dto = (LibrariesDto)Service().GetLibraries().Body!;

        var shows = dto.Libraries.Single(l => l.Id == ShowsId);
        Assert.True(shows.Monitored);
        Assert.Equal("Anime", shows.Category);
        Assert.Equal(new MissingLibraryDto(deleted, "Movies"), Assert.Single(dto.Missing));
    }

    [Fact]
    public void Get_IgnoresVirtualFoldersWithUnusableIds_AndKeepsNamesAsPlainData()
    {
        SetLibraries(Folder(MoviesId, "Movies", null), new VirtualFolderInfo { ItemId = "not-a-guid", Name = "Broken" }, new VirtualFolderInfo { ItemId = null, Name = "NoId" });

        var dto = (LibrariesDto)Service().GetLibraries().Body!;

        Assert.Equal(MoviesId, Assert.Single(dto.Libraries).Id);
    }

    [Fact]
    public void Update_SavesExactlyTheChosenLibrariesAndCategories()
    {
        var response = Service().UpdateLibraries(Update((MoviesId, "Movies"), (ShowsId, "anime")));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(
            new[] { (MoviesId, MediaCategory.Movies), (ShowsId, MediaCategory.Anime) },
            _accessor.Current.MonitoredLibraries.Select(l => (l.LibraryId, l.Category)));
        Assert.True(((LibrariesDto)response.Body!).Libraries.Single(l => l.Id == ShowsId).Monitored);
    }

    [Fact]
    public void Update_EmptyList_MonitorsNothing_NotEverything()
    {
        _accessor.Current.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = MoviesId, Category = MediaCategory.Movies });

        var response = Service().UpdateLibraries(Update());

        Assert.Equal(200, response.StatusCode);
        Assert.Empty(_accessor.Current.MonitoredLibraries);
        Assert.All(((LibrariesDto)response.Body!).Libraries, l => Assert.False(l.Monitored));
    }

    [Fact]
    public void Update_DropsConfiguredLibrariesThatNoLongerExistOnlyBecauseTheyAreNotResubmitted()
    {
        _accessor.Current.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = Guid.NewGuid(), Category = MediaCategory.Movies });

        var response = Service().UpdateLibraries(Update((MoviesId, "Movies")));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(MoviesId, Assert.Single(_accessor.Current.MonitoredLibraries).LibraryId);
        Assert.Empty(((LibrariesDto)response.Body!).Missing);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Everything")]
    [InlineData("99")]
    public void Update_MissingOrUnknownCategory_IsRejectedAndNothingChanges(string? category)
    {
        var response = Service().UpdateLibraries(Update((MoviesId, category)));

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(0, _accessor.SaveCount);
    }

    [Fact]
    public void Update_UnknownLibrary_IsRejected()
    {
        var response = Service().UpdateLibraries(Update((Guid.NewGuid(), "Movies")));

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("does not exist", JsonSerializer.Serialize(response.Body), StringComparison.Ordinal);
        Assert.Equal(0, _accessor.SaveCount);
    }

    [Fact]
    public void Update_EmptyIdAndDuplicates_AreRejected()
    {
        Assert.Equal(400, Service().UpdateLibraries(Update((Guid.Empty, "Movies"))).StatusCode);
        Assert.Equal(400, Service().UpdateLibraries(Update((MoviesId, "Movies"), (MoviesId, "Anime"))).StatusCode);
        Assert.Equal(0, _accessor.SaveCount);
    }

    [Fact]
    public void Update_NullRequestOrList_IsRejected()
    {
        Assert.Equal(400, Service().UpdateLibraries(null).StatusCode);
        Assert.Equal(400, Service().UpdateLibraries(new LibrariesUpdate(null)).StatusCode);
    }

    [Fact]
    public void Update_WhenTheSaveGuardRejectsIt_Returns400WithTheReasons()
    {
        _accessor.SaveThrows = new PluginConfigurationValidationException(new[] { "MonitoredLibraries[0] refers to a library that does not exist (it may have been deleted)." });

        var response = Service().UpdateLibraries(Update((MoviesId, "Movies")));

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("deleted", JsonSerializer.Serialize(response.Body), StringComparison.Ordinal);
    }

    [Fact]
    public void Update_PreservesEverythingElseInTheConfiguration()
    {
        _accessor.Current.DigestEnabled = true;
        _accessor.Current.SmtpHost = "smtp.example.com";
        _accessor.Current.MaxEntriesPerDigest = 42;

        Service().UpdateLibraries(Update((MoviesId, "Movies")));

        Assert.True(_accessor.Current.DigestEnabled);
        Assert.Equal("smtp.example.com", _accessor.Current.SmtpHost);
        Assert.Equal(42, _accessor.Current.MaxEntriesPerDigest);
    }

    [Fact]
    public void JellyfinFailure_Propagates_SoTheControllerAnswersWithAGenericError()
    {
        _libraryManager.Setup(m => m.GetVirtualFolders()).Throws(new InvalidOperationException("db locked"));

        Assert.Throws<InvalidOperationException>(() => Service().GetLibraries());
        Assert.Throws<InvalidOperationException>(() => Service().UpdateLibraries(Update((MoviesId, "Movies"))));
        Assert.Equal(0, _accessor.SaveCount);
    }

    private sealed class FakeAccessor : ISmtpConfigurationAccessor
    {
        public PluginConfiguration Current { get; private set; } = new();

        public int SaveCount { get; private set; }

        public Exception? SaveThrows { get; set; }

        public PluginConfiguration GetCopy()
        {
            var copy = new PluginConfiguration
            {
                SmtpHost = Current.SmtpHost,
                DigestEnabled = Current.DigestEnabled,
                MaxEntriesPerDigest = Current.MaxEntriesPerDigest
            };
            foreach (var library in Current.MonitoredLibraries)
            {
                copy.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = library.LibraryId, Category = library.Category });
            }

            return copy;
        }

        public void Save(PluginConfiguration configuration)
        {
            if (SaveThrows is not null)
            {
                throw SaveThrows;
            }

            Current = configuration;
            SaveCount++;
        }
    }
}
