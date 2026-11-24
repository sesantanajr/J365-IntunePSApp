using System;
using System.Collections.Generic;
using System.Linq;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;
using Pass = J365.IntunePSApp.Core.IwauProgress.Pass;

namespace J365.IntunePSApp.Tests;

/// <summary>3.0.2: a percentage that follows the real work while the .intunewin is created, beside each item of the batch.</summary>
public class Release302Tests
{
    private const long MB = 1024 * 1024;

    // What IntuneWinAppUtil.exe 1.8.7 prints with the output redirected and -q (from its code), one line per step.
    private static readonly string[] IwauRun =
    {
        @"INFO   Validating parameters",
        @"INFO   Validated parameters within 9 milliseconds",
        @"INFO   Compressing the source folder 'C:\pkg\Source' to 'C:\tmp\w\6f1c\IntuneWinPackage\Contents\IntunePackage.intunewin'",
        @"INFO   Calculated size for folder 'C:\pkg\Source' is 734003200 within 2 milliseconds",
        @"INFO   Compressed folder 'C:\pkg\Source' successfully within 6210 milliseconds",
        @"INFO   Checking file type",
        @"INFO   Checked file type within 3 milliseconds",
        @"INFO   Encrypting file 'C:\tmp\w\6f1c\IntuneWinPackage\Contents\IntunePackage.intunewin'",
        @"INFO   'C:\tmp\w\6f1c\IntuneWinPackage\Contents\IntunePackage.intunewin' has been encrypted successfully within 1990 milliseconds",
        @"INFO   Computing SHA256 hash for C:\tmp\w\6f1c\IntuneWinPackage\Contents\0b7d",
        @"INFO   Computed SHA256 hash for 'C:\tmp\w\6f1c\IntuneWinPackage\Contents\0b7d' within 4100 milliseconds",
        @"INFO   Computing SHA256 hash for C:\tmp\w\6f1c\IntuneWinPackage\Contents\IntunePackage.intunewin",
        @"INFO   Computed SHA256 hash for C:\tmp\w\6f1c\IntuneWinPackage\Contents\IntunePackage.intunewin within 4050 milliseconds",
        @"INFO   Copying encrypted file from 'C:\tmp\w\6f1c\IntuneWinPackage\Contents\0b7d' to 'C:\tmp\w\6f1c\IntuneWinPackage\Contents\IntunePackage.intunewin'",
        @"INFO   File 'C:\tmp\w\6f1c\IntuneWinPackage\Contents\IntunePackage.intunewin' got updated successfully within 1400 milliseconds",
        @"INFO   Generating detection XML file 'C:\tmp\w\6f1c\IntuneWinPackage\Metadata\Detection.xml'",
        @"INFO   Generated detection XML file within 8 milliseconds",
        @"INFO   Compressing folder 'C:\tmp\w\6f1c\IntuneWinPackage' to 'C:\out\_iwau_1a2b\Invoke-AppDeployToolkit.intunewin'",
        @"INFO   Calculated size for folder 'C:\tmp\w\6f1c\IntuneWinPackage' is 733003300 within 0 milliseconds",
        @"INFO   Compressed folder 'C:\tmp\w\6f1c\IntuneWinPackage' successfully within 2900 milliseconds",
        @"INFO   Removing temporary files",
        @"INFO   Removed temporary files within 210 milliseconds",
        @"INFO   File 'C:\out\_iwau_1a2b\Invoke-AppDeployToolkit.intunewin' has been generated successfully",
        "",
        @"INFO   Done!!!",
    };

    private static double SharesBefore(Pass pass) => IwauProgress.Shares.Take((int)pass).Sum();

    [Fact]
    public void Each_line_of_IntuneWinAppUtil_begins_its_pass()
    {
        var expected = new Dictionary<int, Pass>
        {
            [2] = Pass.Zip, [7] = Pass.Encrypt, [9] = Pass.Mac, [11] = Pass.Digest, [13] = Pass.Copy,
            [15] = Pass.Metadata, [17] = Pass.Store, [20] = Pass.Cleanup,
        };
        int hashes = 0;
        for (int i = 0; i < IwauRun.Length; i++)
        {
            var pass = IwauProgress.PassOf(IwauRun[i], hashes);
            if (pass is Pass.Mac or Pass.Digest) hashes++;
            Assert.Equal(expected.TryGetValue(i, out var p) ? p : Pass.None, pass);
        }
        Assert.Equal(1, IwauProgress.Shares.Sum(), 6);
    }

    [Fact]
    public void The_percentage_of_IntuneWinAppUtil_follows_the_bytes_of_every_pass()
    {
        long content = 700 * MB, zip = 699 * MB;
        var t = new IwauProgress(content);
        Assert.False(t.Recognized);
        long read = 2 * MB, written = 0;
        (long, long)? Io() => (read, written);
        var seen = new List<double>();
        void At(double expected, long work = 0, long output = 0)
        {
            double f = t.Fraction(Io(), work, output);
            Assert.Equal(expected, f, 3);
            seen.Add(f);
        }

        t.OnLine(IwauRun[0], Io());
        Assert.False(t.Recognized);
        t.OnLine(IwauRun[2], Io());                               // the compression reads the content once
        Assert.True(t.Recognized);
        read += content / 2; written += zip / 2;
        At(0.32 * 0.5);
        read += content / 2; written += zip / 2;
        At(0.32);
        t.OnLine(IwauRun[7], Io(), () => zip);                    // then every pass goes over the zip
        read += zip / 4; written += zip / 4;
        At(0.32 + 0.09 * 0.25);
        read += zip * 3 / 4; written += zip * 3 / 4;
        t.OnLine(IwauRun[9], Io());                               // HMAC: reads only, and the bar moves
        read += zip / 2;
        At(SharesBefore(Pass.Mac) + 0.19 * 0.5);
        read += zip / 2;
        t.OnLine(IwauRun[11], Io());                              // SHA256 of the zip: reads only
        read += zip / 2;
        At(SharesBefore(Pass.Digest) + 0.19 * 0.5);
        read += zip / 2;
        t.OnLine(IwauRun[13], Io());                              // the encrypted file copied over the zip
        read += zip; written += zip;
        At(SharesBefore(Pass.Copy) + 0.06);
        t.OnLine(IwauRun[15], Io());
        t.OnLine(IwauRun[17], Io());                              // System.IO.Packaging: to its buffer first...
        read += zip; written += zip;
        At(SharesBefore(Pass.Store) + 0.13 * 0.5, output: 0);
        read += zip; written += zip;                              // ...then into the .intunewin when it closes
        At(SharesBefore(Pass.Store) + 0.13, output: zip);
        t.OnLine(IwauRun[20], Io());
        At(0.98, output: zip);
        t.OnLine(IwauRun[24], Io());
        At(0.98, output: zip);
        for (int i = 1; i < seen.Count; i++) Assert.True(seen[i] >= seen[i - 1], "the progress went back");
        Assert.Equal(Pass.Cleanup, t.Current);
    }

    [Fact]
    public void Without_the_counters_of_the_process_the_files_it_writes_are_measured()
    {
        long content = 100 * MB, zip = 90 * MB;
        var t = new IwauProgress(content);
        t.OnLine(IwauRun[2], null);
        Assert.Equal(0.32 * 0.45, t.Fraction(null, 45 * MB, 0), 3);
        t.OnLine(IwauRun[7], null, () => zip);
        Assert.Equal(0.32 + 0.09 * 0.5, t.Fraction(null, zip + zip / 2, 0), 3);
        t.OnLine(IwauRun[9], null);                               // nothing to measure: the bar waits at the start of the pass
        Assert.Equal(SharesBefore(Pass.Mac), t.Fraction(null, 2 * zip, 0), 3);
        t.OnLine(IwauRun[17], null);
        Assert.Equal(SharesBefore(Pass.Store) + 0.13 * 0.5, t.Fraction(null, zip, zip / 2), 3);
    }

    [Fact]
    public void A_line_that_comes_back_never_moves_the_bar_backwards()
    {
        var t = new IwauProgress(10 * MB);
        t.OnLine(IwauRun[7], (0, 0), () => 10 * MB);
        double before = t.Fraction((5 * MB, 0), 0, 0);
        t.OnLine(IwauRun[2], (5 * MB, 0));                         // an earlier pass: ignored
        Assert.Equal(Pass.Encrypt, t.Current);
        Assert.True(t.Fraction((5 * MB, 0), 0, 0) >= before);
        Assert.Equal(Pass.None, IwauProgress.PassOf("Hello", 0));
        Assert.Equal(Pass.None, IwauProgress.PassOf(null, 0));
        Assert.Equal(Pass.Zip, IwauProgress.PassOf("Compressing the source folder 'a' to 'b'", 0));
    }

    [Fact]
    public void The_built_in_packager_gives_each_pass_the_share_of_the_time_it_takes()
    {
        // An installer stored as it is (the usual package): each pass is about a third, not 70% for the zip.
        var installer = IntuneWinBuilder.NativeShares(new[] { (700 * MB, true), (12 * MB, false) });
        Assert.InRange(installer.Zip, 0.25, 0.45);
        Assert.InRange(installer.Encrypt, 0.25, 0.45);
        // A folder of files to compress: the zip takes most of the time.
        var folder = IntuneWinBuilder.NativeShares(new[] { (300 * MB, false) });
        Assert.True(folder.Zip > 0.75, folder.Zip.ToString());
        Assert.Equal(10, IntuneWinBuilder.NativeCost(MB, false) / IntuneWinBuilder.NativeCost(MB, true), 6);
        var empty = IntuneWinBuilder.NativeShares(Array.Empty<(long, bool)>());
        Assert.Equal(1d / 3, empty.Zip, 6);
    }

    private static BatchItem Item() => new BatchItem(new CatalogApp
    {
        Id = "Contoso.App", Name = "App", Publisher = "Contoso", Version = "1.0",
        Installers = new List<CatalogInstaller> { new CatalogInstaller { Type = "exe", Arch = "x64" } }
    }, null);

    [Fact]
    public void Each_item_of_the_batch_shows_the_percentage_of_the_step_it_is_in()
    {
        var item = Item();
        Assert.Equal("", item.PercentText);
        item.Update(BatchState.Downloading, "Downloading...", 0, measured: false);
        Assert.Equal("", item.PercentText);                          // the size of the download is not known yet
        item.Update(BatchState.Downloading, "Downloading...", 47.9);
        Assert.Equal("47%", item.PercentText);                       // never rounded up before it is there
        item.Update(BatchState.Analyzing, "Analysing...", 0, measured: false);
        Assert.False(item.HasPercent);
        Assert.Equal("", item.PercentText);
        item.Update(BatchState.Building, "Checking...", 0);
        Assert.Equal("0%", item.PercentText);
        item.Update(BatchState.Building, "Creating the .intunewin file", 63.4);
        Assert.Equal("63%", item.PercentText);
        Assert.Null(item.Remaining);                                 // too early to tell
        Assert.Equal("Creating the .intunewin file", item.MessageLine);

        item.Update(BatchState.Publishing, "Signing in", 0, measured: false);
        Assert.Equal("", item.PercentText);
        BatchPackager.OnPublishLine(item, "> Uploading 48.2 MB...");
        Assert.Equal("", item.PercentText);
        BatchPackager.OnPublishLine(item, "  42 %");
        Assert.Equal("42%", item.PercentText);
        BatchPackager.OnPublishLine(item, "Waiting for Intune");     // a message keeps the number of the step
        Assert.Equal("42%", item.PercentText);
        BatchPackager.OnPublishLine(item, "> Committing...");        // a new step has no number until it says one
        Assert.Equal("", item.PercentText);
        Assert.Equal(42, item.Percent);
        item.Finish(BatchState.Published, "done");
        Assert.Equal("", item.PercentText);
        Assert.Equal("done", item.MessageLine);
    }

    [Theory]
    [InlineData(10, 50, 10)]
    [InlineData(30, 75, 10)]
    [InlineData(4, 20, 16)]
    public void The_time_left_follows_the_pace_so_far(double elapsed, double percent, double left)
    {
        Assert.Equal(left, BatchItem.Estimate(TimeSpan.FromSeconds(elapsed), percent)!.Value.TotalSeconds, 6);
    }

    [Theory]
    [InlineData(1, 50)]    // too early
    [InlineData(10, 2)]    // too little done
    [InlineData(10, 100)]  // over
    public void No_time_left_is_guessed_too_early(double elapsed, double percent)
    {
        Assert.Null(BatchItem.Estimate(TimeSpan.FromSeconds(elapsed), percent));
    }

    [Fact]
    public void The_status_bar_says_one_package_in_the_singular()
    {
        Lang.With(Loc.PortugueseCode, () =>
        {
            Assert.Equal("Gerando 1 pacote em lote...", Loc.Count("Batch_Status", 1));
            Assert.Equal("Gerando 3 pacotes em lote...", Loc.Count("Batch_Status", 3));
            Assert.Equal("Criando o arquivo .intunewin · cerca de 35 s restantes", Loc.T("Batch_ItemEta", "Criando o arquivo .intunewin", "35 s"));
        });
        Lang.With(Loc.EnglishCode, () =>
        {
            Assert.Equal("Generating 1 package in a batch...", Loc.Count("Batch_Status", 1));
            Assert.Equal("Generating 3 packages in a batch...", Loc.Count("Batch_Status", 3));
        });
    }
}
