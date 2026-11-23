using System;
using System.IO;
using System.Threading;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

namespace J365.IntunePSApp.Tests;

/// <summary>2.6.1: the check for new versions never says "up to date" when it read nothing, and survives a blocked API.</summary>
public class Release261Tests
{
    [Theory]
    [InlineData("https://github.com/sesantanajr/J365-IntunePSApp/releases/tag/v2.6.0", "2.6.0")]
    [InlineData("https://github.com/owner/repo/releases/tag/2.6.1", "2.6.1")]
    [InlineData("https://github.com/owner/repo/releases/tag/v10.0.1.2/", "10.0.1.2")]
    [InlineData("https://github.com/owner/repo/releases/tag/v2.6.0?x=1", "2.6.0")]
    [InlineData("https://github.com/owner/repo/releases", null)]
    [InlineData("https://github.com/owner/repo/releases/tag/v2.6.0-beta", null)]
    [InlineData("https://github.com/owner/repo/releases/tag/catalog", null)]
    [InlineData(null, null)]
    public void The_latest_version_is_read_from_the_address_of_the_release_page(string url, string expected)
        => Assert.Equal(expected, AppUpdater.VersionFromReleaseUrl(url));

    [Fact]
    public void A_release_known_by_its_version_has_the_files_of_every_release_of_the_project()
    {
        var r = AppUpdater.ReleaseFromVersion("2.6.1");
        Assert.Equal("2.6.1", r.Version);
        Assert.Equal("J365-IntunePSApp-2.6.1-win-x64.zip", r.ZipName);
        Assert.Equal(AppInfo.RepoUrl + "/releases/download/v2.6.1/J365-IntunePSApp-2.6.1-win-x64.zip", r.ZipUrl);
        Assert.Equal(AppInfo.RepoUrl + "/releases/download/v2.6.1/SHA256SUMS.txt", r.SumsUrl);
        Assert.Equal(AppInfo.RepoUrl + "/releases/tag/v2.6.1", r.PageUrl);
        // The zip name is the one the release workflow writes and the updater looks for in SHA256SUMS.txt.
        Assert.Equal("ABC", AppUpdater.HashFor(new string('a', 61) + "ABC  " + r.ZipName, r.ZipName)?.Substring(61));
    }

    [Fact]
    public void A_release_source_that_does_not_answer_is_an_error_not_an_answer()
    {
        // A mirror (J365_UPDATE_API) answers alone: nothing else is asked, so nothing leaves this computer.
        string before = Environment.GetEnvironmentVariable("J365_UPDATE_API");
        Environment.SetEnvironmentVariable("J365_UPDATE_API", "http://127.0.0.1:9/releases/latest");
        try
        {
            var ex = Assert.ThrowsAny<IOException>(() => AppUpdater.GetLatestAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.StartsWith(Loc.T("Update_ErrNoInfo"), ex.Message);
        }
        finally { Environment.SetEnvironmentVariable("J365_UPDATE_API", before); }
    }

    [Fact]
    public void Up_to_date_names_the_version_and_a_failed_check_says_why()
    {
        Assert.Contains("2.6.1", Loc.T("Settings_UpToDate", "2.6.1", "23/11/2026 10:06"));
        Assert.Contains("HTTP 403", Loc.T("Settings_UpdateCheckFailed", "HTTP 403"));
    }
}
