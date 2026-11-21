using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using J365.IntunePSApp.Core;
using Xunit;

namespace J365.IntunePSApp.Tests;

public class PackageRevisionTests
{
    [Fact]
    public void Output_location_and_migration_do_not_invalidate_a_build()
    {
        var s = Fakes.Settings();
        var before = PackageRevision.Fingerprint(s);
        var copy = s.Clone();
        copy.OutputRoot = @"D:\NewOutput";
        PackageBuilder.Migrate(copy);
        if (string.IsNullOrWhiteSpace(copy.DetectionNamePattern)) copy.DetectionNamePattern = copy.UninstallPattern;
        Assert.Equal(before, PackageRevision.Fingerprint(copy));
        Assert.Equal(before, PackageRevision.Fingerprint(s));
    }

    [Fact]
    public void Saving_changed_settings_does_not_make_the_built_package_current()
    {
        using var tmp = new TempFolder();
        var s = Fakes.Settings();
        PackageRevision.Save(tmp.Path, s);
        Assert.True(PackageRevision.Matches(tmp.Path, s));
        s.InstallArgs += " /new-option";
        Json.Save(Path.Combine(tmp.Path, PackageLayout.ProjectFile), s);
        Assert.False(PackageRevision.Matches(tmp.Path, PackageBuilder.LoadProject(Path.Combine(tmp.Path, PackageLayout.ProjectFile))));
        PackageRevision.Save(tmp.Path, s);
        Assert.True(PackageRevision.Matches(tmp.Path, s));
    }

    [Theory]
    [InlineData("invalid JSON")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"Schema\":2,\"SettingsHash\":\"unknown\"}")]
    public void Broken_or_unknown_revision_is_not_treated_as_a_legacy_package(string content)
    {
        using var tmp = new TempFolder();
        var s = Fakes.Settings();
        Assert.True(PackageRevision.Matches(tmp.Path, s));
        File.WriteAllText(Path.Combine(tmp.Path, PackageRevision.FileName), content);
        Assert.False(PackageRevision.Matches(tmp.Path, s));
    }

    [Fact]
    public void Reopening_a_changed_project_does_not_offer_the_previous_build()
    {
        using var tmp = new TempFolder();
        var s = Fakes.Settings();
        Directory.CreateDirectory(Path.Combine(tmp.Path, PackageLayout.PackageDir));
        var intune = Directory.CreateDirectory(Path.Combine(tmp.Path, PackageLayout.IntuneDir));
        File.WriteAllText(Path.Combine(intune.FullName, "app.intunewin"), "test");
        string project = Path.Combine(tmp.Path, PackageLayout.ProjectFile);
        PackageRevision.Save(tmp.Path, s);
        Assert.NotNull(PackageBuilder.TryLoadResult(project, s));
        s.Owner = "Another owner";
        Assert.Null(PackageBuilder.TryLoadResult(project, s));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Parallel_reservations_never_reuse_or_damage_an_existing_package(bool preexisting)
    {
        using var tmp = new TempFolder();
        string existing = Path.Combine(tmp.Path, "Test_1");
        if (preexisting) { Directory.CreateDirectory(existing); File.WriteAllText(Path.Combine(existing, "keep.txt"), "existing package"); }
        var paths = new ConcurrentBag<string>();
        Parallel.For(0, 32, _ =>
        {
            using var reservation = PackageDirectory.Reserve(tmp.Path, "Test_1");
            paths.Add(reservation.Folder);
            File.WriteAllText(Path.Combine(reservation.Folder, "owned.txt"), "new package");
        });
        Assert.Equal(32, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        if (preexisting)
        {
            Assert.DoesNotContain(existing, paths);
            Assert.Equal("existing package", File.ReadAllText(Path.Combine(existing, "keep.txt")));
        }
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.build-lock"));
    }

    [Fact]
    public void History_scan_can_be_cancelled()
    {
        using var tmp = new TempFolder();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "package"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PackageBuilder.History(tmp.Path, cts.Token));
    }
}
