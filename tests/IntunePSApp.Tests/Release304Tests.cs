using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

namespace J365.IntunePSApp.Tests;

/// <summary>3.0.4: the upload to Intune says how far it is every point, and the publishing window shows the percentage and the time left.</summary>
public class Release304Tests
{
    [Fact]
    public void The_upload_says_every_point_at_most_once_a_second()
    {
        string script = PackageBuilder.EmbeddedText("Publish-ToIntune.ps1");
        Ps.AssertParses(script);
        Assert.True(Ps.IsAscii(script));
        // A line every 10% left the bar still for a minute on a large package.
        Assert.DoesNotContain("$lastShown + 10", script);
        Assert.Contains("($percent -gt $lastShown -and $shownClock.ElapsedMilliseconds -ge 1000) -or $sent -ge $encryptedSize", script);
        Assert.Contains("$shownClock = [System.Diagnostics.Stopwatch]::StartNew()", script);
        Assert.Contains("Write-Host ('  ' + $percent + ' %')", script);
    }

    [Fact]
    public void Each_point_of_the_upload_moves_the_item_of_the_batch()
    {
        var item = new BatchItem(new CatalogApp { Id = "Contoso.App", Name = "App", Publisher = "Contoso", Version = "1.0" }, null);
        item.Update(BatchState.Publishing, "", 0, measured: false);
        BatchPackager.OnPublishLine(item, "> Uploading 770 MB...");
        foreach (int p in new[] { 1, 2, 3, 47 })
        {
            BatchPackager.OnPublishLine(item, "  " + p + " %");
            Assert.Equal(p, item.Percent);
            Assert.Equal(p + "%", item.PercentText);
        }
    }

    [Fact]
    public void The_publishing_window_says_the_time_left_in_both_languages()
    {
        Lang.With(Loc.PortugueseCode, () =>
        {
            Assert.Equal("cerca de 2 min 05 s restantes", Loc.T("Busy_Eta", "2 min 05 s"));
            Assert.DoesNotContain("PowerShell", Loc.T("Ed_PublishRunningInApp"));
        });
        Lang.With(Loc.EnglishCode, () =>
        {
            Assert.Equal("about 2 min 05 s left", Loc.T("Busy_Eta", "2 min 05 s"));
            Assert.DoesNotContain("PowerShell", Loc.T("Ed_PublishRunningInApp"));
        });
    }
}
