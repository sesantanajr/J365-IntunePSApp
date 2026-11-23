using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

namespace J365.IntunePSApp.Tests;

/// <summary>
/// 2.7.0: the check for new versions reads the files of the latest release first (github.com, no API), so the limit of
/// 60 API requests an hour that a company network shares never makes a copy believe it is up to date.
/// </summary>
public class Release27UpdateTests
{
    // The SHA256SUMS.txt the release workflow writes (2.6.2, as published).
    private const string Sums262 =
        "2C142F9FF67D1575DABD395C7803751FF9283323C82450939AAE85D98D8B016F  J365-IntunePSApp-2.6.2-source.zip\r\n" +
        "D8FEC970604D02B4D6D01127F135EC800F1AAF3A623F5927C6154CAC5D2B7CBD  J365-IntunePSApp-2.6.2-win-x64.zip\r\n";

    [Fact]
    public void The_checksums_of_the_latest_release_give_its_version_and_files()
    {
        var r = AppUpdater.ReleaseFromSums(Sums262);
        Assert.NotNull(r);
        Assert.Equal("2.6.2", r.Version);
        Assert.Equal(AppUpdater.SourceFiles, r.Source);
        Assert.Equal("J365-IntunePSApp-2.6.2-win-x64.zip", r.ZipName);
        Assert.Equal(AppInfo.RepoUrl + "/releases/download/v2.6.2/J365-IntunePSApp-2.6.2-win-x64.zip", r.ZipUrl);
        Assert.Equal(AppInfo.RepoUrl + "/releases/download/v2.6.2/SHA256SUMS.txt", r.SumsUrl);
        // The hash the download is checked against is in the same file.
        Assert.Equal("D8FEC970604D02B4D6D01127F135EC800F1AAF3A623F5927C6154CAC5D2B7CBD", AppUpdater.HashFor(Sums262, r.ZipName));
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000 *J365-IntunePSApp-10.1.0-win-x64.zip", "10.1.0")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000  j365-intunepsapp-2.7.0-WIN-X64.zip\n", "2.7.0")]
    public void Checksum_lines_are_read_in_both_formats(string sums, string version)
        => Assert.Equal(version, AppUpdater.ReleaseFromSums(sums)?.Version);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>Not Found</html>")]
    [InlineData("2C142F9FF67D1575DABD395C7803751FF9283323C82450939AAE85D98D8B016F  J365-IntunePSApp-2.6.2-source.zip")]
    [InlineData("2C142F9F  J365-IntunePSApp-2.6.2-win-x64.zip")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000  J365-IntunePSApp-nightly-win-x64.zip")]
    public void Checksums_without_the_application_zip_name_no_release(string sums)
        => Assert.Null(AppUpdater.ReleaseFromSums(sums));

    [Fact]
    public void The_latest_files_are_asked_on_github_com_not_on_the_api()
    {
        Assert.Equal("https://github.com/" + AppInfo.GitHubRepo + "/releases/latest/download/SHA256SUMS.txt", AppInfo.LatestSumsUrl);
        Assert.DoesNotContain("api.github.com", AppInfo.LatestSumsUrl);
    }

    [Fact]
    public void A_reached_api_limit_says_so_and_when_it_frees_up()
    {
        Lang.With(Loc.EnglishCode, () =>
        {
            var reset = DateTimeOffset.UtcNow.AddMinutes(37);
            using var limited = new HttpResponseMessage(HttpStatusCode.Forbidden);
            limited.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            limited.Headers.TryAddWithoutValidation("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString());
            string text = Http.Describe(limited);
            Assert.StartsWith("HTTP 403 - ", text);
            Assert.Contains("GitHub API limit", text);
            Assert.Contains(reset.ToLocalTime().ToString("t", Loc.Instance.Culture), text);

            // A refusal that is not the limit stays a plain status.
            using var forbidden = new HttpResponseMessage(HttpStatusCode.Forbidden);
            forbidden.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "12");
            Assert.Equal("HTTP 403", Http.Describe(forbidden));
            using var missing = new HttpResponseMessage(HttpStatusCode.NotFound);
            Assert.Equal("HTTP 404", Http.Describe(missing));
        });
    }

    [Theory]
    [InlineData("https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/releases/tag/4.1.8", "4.1.8", "4.1.8")]
    [InlineData("https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/releases/tag/v4.2.0", "4.2.0", "v4.2.0")]
    [InlineData("https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/releases/tag/4.1/", "4.1", "4.1")]
    public void The_latest_toolkit_is_read_from_the_address_of_its_release_page(string url, string version, string tag)
    {
        var r = ToolsService.PsadtReleaseFromUrl(url);
        Assert.NotNull(r);
        Assert.Equal(version, r.Value.Version);
        Assert.Equal($"https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/releases/download/{tag}/PSAppDeployToolkit_Template_v4.zip", r.Value.Url);
    }

    [Theory]
    [InlineData("https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/releases/tag/3.10.2")]
    [InlineData("https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/releases/tag/4.2.0-beta1")]
    [InlineData("https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/releases")]
    [InlineData("https://github.com/someone/else/releases/tag/4.1.8")]
    [InlineData(null)]
    public void Only_a_4x_release_of_the_toolkit_is_taken_from_a_page_address(string url)
        => Assert.Null(ToolsService.PsadtReleaseFromUrl(url));

    [Fact]
    public void The_newest_stable_toolkit_with_a_template_is_taken_from_the_api()
    {
        const string json = @"[
  { ""tag_name"": ""4.2.0-beta1"", ""prerelease"": true, ""draft"": false, ""assets"": [ { ""name"": ""PSAppDeployToolkit_Template_v4.zip"", ""browser_download_url"": ""https://example.test/beta.zip"" } ] },
  { ""tag_name"": ""3.10.2"", ""prerelease"": false, ""draft"": false, ""assets"": [ { ""name"": ""PSAppDeployToolkit_Template_v3.zip"", ""browser_download_url"": ""https://example.test/v3.zip"" } ] },
  { ""tag_name"": ""4.1.8"", ""prerelease"": false, ""draft"": false, ""assets"": [ { ""name"": ""PSAppDeployToolkit_Template_v4.zip"", ""browser_download_url"": ""https://example.test/418.zip"" } ] }
]";
        var r = ToolsService.ParsePsadtReleases(json);
        Assert.Equal(("4.1.8", "https://example.test/418.zip"), r.Value);
        Assert.Null(ToolsService.ParsePsadtReleases(null));
        Assert.Null(ToolsService.ParsePsadtReleases("{ \"message\": \"API rate limit exceeded\" }"));
        Assert.Null(ToolsService.ParsePsadtReleases("not json"));
    }

    /// <summary>
    /// Against the real GitHub (only with J365_LIVE_TESTS=1): the latest release is found through its files, without the API.
    /// </summary>
    [Fact]
    public async Task Live_the_latest_release_and_toolkit_are_found_without_the_api()
    {
        if (Environment.GetEnvironmentVariable("J365_LIVE_TESTS") != "1") return;
        var r = await AppUpdater.GetLatestAsync(CancellationToken.None);
        Assert.Equal(AppUpdater.SourceFiles, r.Source);
        Assert.Matches(@"^\d+\.\d+\.\d+$", r.Version);
        Assert.True(await AppUpdater.ZipSizeAsync(r, CancellationToken.None) > 10_000_000);
        var psadt = ToolsService.PsadtReleaseFromUrl(await Http.FinalUrlAsync("https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/releases/latest", 30, CancellationToken.None));
        Assert.NotNull(psadt);
        Assert.StartsWith("4.", psadt.Value.Version);
    }
}
