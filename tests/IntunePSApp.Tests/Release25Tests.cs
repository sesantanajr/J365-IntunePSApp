using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

namespace J365.IntunePSApp.Tests;

public class Release25Tests
{
    [Fact]
    public void Generated_metadata_separates_name_and_version_and_keeps_optional_fields()
    {
        using var tmp = new TempFolder();
        var s = Fakes.Settings();
        s.AppName = "Amazon WorkSpaces";
        s.AppVersion = "5.37.0.3866";
        s.AppVendor = "Amazon";
        s.CompanyName = "Jornada 365";
        s.PrivacyUrl = "https://example.com/privacy";
        var r = new BuildResult { PackageFolder = tmp.Path, IntuneFolder = tmp.Path, IntuneWinFile = "app.intunewin", DetectionScript = "detect.ps1" };
        typeof(PackageBuilder).GetMethod("WriteCompanions", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { s, r });
        using var json = JsonDocument.Parse(File.ReadAllText(r.MetadataFile));
        Assert.Equal(s.AppName, json.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(s.AppVersion, json.RootElement.GetProperty("appVersion").GetString());
        Assert.Equal("Amazon", json.RootElement.GetProperty("developer").GetString());
        Assert.Equal("Jornada 365", json.RootElement.GetProperty("owner").GetString());
        Assert.Equal(s.PrivacyUrl, json.RootElement.GetProperty("privacyInformationUrl").GetString());
        s.Developer = "Explicit developer"; s.Owner = "IT";
        typeof(PackageBuilder).GetMethod("WriteCompanions", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { s, r });
        using var explicitJson = JsonDocument.Parse(File.ReadAllText(r.MetadataFile));
        Assert.Equal("IT", explicitJson.RootElement.GetProperty("owner").GetString());
        Assert.Equal("Explicit developer", explicitJson.RootElement.GetProperty("developer").GetString());
    }

    [Theory]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/secret", false)]
    [InlineData("example.com/privacy", false)]
    [InlineData("https://example.com/privacy", true)]
    [InlineData("", true)]
    public void Privacy_URL_is_validated_before_packaging(string url, bool valid)
    {
        var s = Fakes.Settings(); s.PrivacyUrl = url;
        Assert.Equal(!valid, PackageBuilder.Validate(s).Errors.Contains(Loc.T("Val_WebUrl", url)));
    }

    [Fact]
    public void Old_projects_load_and_new_metadata_round_trips()
    {
        var old = JsonSerializer.Deserialize<PackageSettings>("{\"AppName\":\"7-Zip\",\"AppVersion\":\"25.01\"}", Json.Options);
        Assert.Equal("", old.PrivacyUrl);
        old.PrivacyUrl = "https://example.com/privacy"; old.Developer = "Developer"; old.Owner = "IT";
        var clone = old.Clone();
        Assert.Equal(old.PrivacyUrl, clone.PrivacyUrl); Assert.Equal(old.Owner, clone.Owner); Assert.Equal(old.Developer, clone.Developer);
    }

    [Fact]
    public void Concurrent_saves_leave_valid_JSON_and_no_staging_files()
    {
        using var tmp = new TempFolder();
        string file = Path.Combine(tmp.Path, "project.json");
        Parallel.For(0, 30, i => Json.Save(file, new PackageSettings { AppName = "App " + i, AppVersion = i.ToString() }));
        var saved = JsonSerializer.Deserialize<PackageSettings>(File.ReadAllText(file), Json.Options);
        Assert.Equal("App " + saved.AppVersion, saved.AppName);
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.tmp"));
    }
}
