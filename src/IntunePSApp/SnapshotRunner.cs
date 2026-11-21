using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;

namespace J365.IntunePSApp
{
    /// <summary>
    /// Capture mode, used for visual checks and for the screenshots of the documentation:
    ///   J365.IntunePSApp.exe --snapshot &lt;folder&gt; [--lang en|pt-BR] [--snapshot-search &lt;text&gt;]
    ///                        [--snapshot-installer &lt;file&gt; | --snapshot-project &lt;project.json&gt; [--snapshot-include-deps]]
    ///                        [--snapshot-build] [--snapshot-out &lt;folder&gt;] [--snapshot-setup] [--snapshot-relang en|pt-BR]
    /// The window stays off-screen, every page is rendered to a PNG file and the application exits.
    /// </summary>
    public static class SnapshotRunner
    {
        public static async Task RunAsync(MainWindow w, string dir)
        {
            Directory.CreateDirectory(dir);
            bool pt = Loc.Instance.IsPortuguese;
            try
            {
                async Task Shot(string name)
                {
                    await Task.Delay(500);
                    w.ClearToasts();
                    w.UpdateLayout();
                    var root = (FrameworkElement)w.Content;
                    var rtb = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(root);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using var fs = File.Create(Path.Combine(dir, name + ".png"));
                    enc.Save(fs);
                }

                // A dialog drawn off-screen, captured, then closed.
                async Task ShotWindow(Views.ConnectWindow win, string name, Action<Views.ConnectWindow> prepare)
                {
                    win.WindowStartupLocation = WindowStartupLocation.Manual;
                    win.Left = -32000; win.Top = 0;
                    win.ShowActivated = false;
                    win.ShowInTaskbar = false;
                    win.Show();
                    prepare?.Invoke(win);
                    await Task.Delay(700);
                    win.UpdateLayout();
                    var root = (FrameworkElement)VisualTreeHelper.GetChild(win, 0);
                    var rtb = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(root);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (var fs = File.Create(Path.Combine(dir, name + ".png"))) enc.Save(fs);
                    win.CloseForSnapshot();
                }

                // Any other dialog, drawn off-screen the same way, captured, then closed.
                async Task ShotDialog(Window win, string name)
                {
                    win.WindowStartupLocation = WindowStartupLocation.Manual;
                    win.Left = -32000; win.Top = 0;
                    win.ShowActivated = false;
                    win.ShowInTaskbar = false;
                    win.Show();
                    await Task.Delay(700);
                    win.UpdateLayout();
                    var root = (FrameworkElement)VisualTreeHelper.GetChild(win, 0);
                    var rtb = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(root);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (var fs = File.Create(Path.Combine(dir, name + ".png"))) enc.Save(fs);
                    win.Close();
                }

                if (App.HasArg("--snapshot-setup"))
                {
                    var setup = w.ShowSetupAsync(refresh: false);
                    await Task.Delay(400);
                    await Shot("00-setup-running");
                    for (int i = 0; i < 600 && w.IsSetupRunning; i++) await Task.Delay(500);
                    await Shot("00-setup-done");
                    w.CloseSetup();
                    await setup;
                }

                w.Navigate("home");
                await Shot("01-home");

                w.Navigate("catalog");
                w.Catalog.FocusSearch(App.Arg("--snapshot-search") ?? "7-Zip");
                await Task.Delay(900);
                await Shot("02-catalog");

                w.Navigate("editor");
                string inst = App.Arg("--snapshot-installer");
                string project = App.Arg("--snapshot-project");
                if (!string.IsNullOrEmpty(project) && File.Exists(project))
                {
                    w.Editor.LoadProject(project);
                    if (App.HasArg("--snapshot-include-deps")) await w.Editor.IncludeDependenciesAsync();
                }
                else if (!string.IsNullOrEmpty(inst) && File.Exists(inst))
                {
                    await w.Editor.AnalyzeFileAsync(inst);
                    var s = w.Editor.Collect();
                    if (string.IsNullOrEmpty(s.CompanyName)) s.CompanyName = pt ? "Contoso Ltda" : "Contoso Ltd";
                    if (string.IsNullOrEmpty(s.WelcomeMessage))
                        s.WelcomeMessage = pt ? $"O {s.AppName} será atualizado para a versão {s.AppVersion}. Salve seus arquivos antes de continuar."
                                              : $"{s.AppName} will be updated to version {s.AppVersion}. Save your files before continuing.";
                    if (string.IsNullOrEmpty(s.CompletionMessage))
                        s.CompletionMessage = pt ? $"Pronto! O {s.AppName} foi atualizado com sucesso." : $"All done! {s.AppName} was updated successfully.";
                    if (s.Actions.Count == 0)
                    {
                        s.Actions.Add(new PackageAction { Type = "RemoveDesktopShortcut", Phase = "PostInstall", Params = { ["Name"] = s.AppName + "*" } });
                        s.Actions.Add(new PackageAction { Type = "SetRegistry", Phase = "PostInstall", Params = { ["Key"] = @"HKLM:\SOFTWARE\Contoso\Inventory", ["Name"] = s.AppName, ["Value"] = s.AppVersion, ["Type"] = "String" } });
                    }
                    w.Editor.LoadSettings(s);
                    if (App.HasArg("--snapshot-include-deps")) await w.Editor.IncludeDependenciesAsync();
                }
                for (int i = 0; i < w.Editor.StepCount; i++)
                {
                    w.Editor.GoStep(i);
                    await Shot($"03-editor-{i + 1}");
                    // Steps taller than the window: a second capture shows their last part.
                    if (w.Editor.ScrollToEnd()) await Shot($"03-editor-{i + 1}-end");
                    // The other moments of the dialog in the preview (nothing open, the last notice).
                    if (w.Editor.PreviewVisible)
                    {
                        var moments = w.Editor.PreviewMoments;
                        foreach (var moment in moments.Skip(1))
                        {
                            w.Editor.ShowPreviewMoment(moment);
                            await Shot($"03-editor-{i + 1}-{moment}");
                        }
                        w.Editor.ShowPreviewMoment(moments[0]);
                    }
                }
                // The bar of a build halfway through.
                if (App.HasArg("--snapshot-progress"))
                {
                    w.Editor.ShowProgressSample(true);
                    await Shot("04-progress");
                    w.Editor.ShowProgressSample(false);
                }
                // Walking through the steps changes nothing: the package must not count as "unsaved".
                File.WriteAllText(Path.Combine(dir, "editor-state.txt"), "unsaved=" + w.Editor.HasUnsavedChanges);
                // Disposable UI regression fixture: no installer, test script or tenant operation is executed.
                if (App.HasArg("--snapshot-state-checks"))
                {
                    var settings = w.Editor.Collect();
                    string fixture = Path.Combine(AppPaths.Temp, "workspace-check-" + Guid.NewGuid().ToString("N"));
                    ConfigStore.Current.OutputFolder = Path.GetDirectoryName(fixture);
                    Directory.CreateDirectory(Path.Combine(fixture, PackageLayout.PackageDir));
                    Directory.CreateDirectory(Path.Combine(fixture, PackageLayout.IntuneDir));
                    File.WriteAllText(Path.Combine(fixture, PackageLayout.IntuneDir, "fixture.intunewin"), "UI fixture only");
                    string fixtureProject = Path.Combine(fixture, PackageLayout.ProjectFile);
                    Json.Save(fixtureProject, settings);
                    PackageRevision.Save(fixture, settings);
                    w.Editor.LoadProject(fixtureProject);
                    if (!w.Editor.CurrentBuildMatchesSettings) throw new InvalidOperationException("A freshly loaded build does not match its settings.");
                    var owner = (System.Windows.Controls.TextBox)w.Editor.FindName("TxtOwner");
                    owner.Text += " changed";
                    if (!w.Editor.HasUnsavedChanges || w.Editor.CurrentBuildMatchesSettings) throw new InvalidOperationException("Editing did not invalidate the build.");
                    w.Editor.ProjectShortcut(System.Windows.Input.Key.S);
                    if (w.Editor.HasUnsavedChanges || w.Editor.CurrentBuildMatchesSettings) throw new InvalidOperationException("Saving incorrectly refreshed the built package.");
                    await Shot("04-editor-needs-rebuild");
                    if (((FrameworkElement)w.Editor.FindName("PublishCallout")).IsEnabled || ((FrameworkElement)w.Editor.FindName("TestActions")).IsEnabled)
                        throw new InvalidOperationException("Stale package actions remain visually enabled.");
                    w.Editor.LoadProject(fixtureProject);
                    if (w.Editor.LastIntuneFolder != null) throw new InvalidOperationException("Reopening offered a stale build.");
                    await Shot("04-editor-reopened-changes");
                    File.WriteAllText(Path.Combine(dir, "workspace-checks.txt"), "load, edit, save, stale build and reopen: passed");
                }
                if (App.HasArg("--snapshot-build"))
                {
                    var s = w.Editor.Collect();
                    string outDir = App.Arg("--snapshot-out");
                    if (!string.IsNullOrEmpty(outDir)) s.OutputRoot = outDir;
                    await w.Editor.BuildAsync(s);
                    await Shot("04-generated");
                    if (!string.IsNullOrEmpty(outDir)) ConfigStore.Current.OutputFolder = outDir;
                    // The publishing dialog of that package, as it opens with the connection of this computer (nothing is sent).
                    if (App.HasArg("--snapshot-publish") && w.Editor.LastIntuneFolder is string intune && Directory.Exists(intune))
                        await ShotDialog(new Views.PublishDialog(IntunePublisher.Prepare(intune), s.AppName + " " + s.AppVersion, null, s.AppName), "09-publish");
                }

                w.Navigate("packages");
                await Shot("05-packages");
                if (App.HasArg("--snapshot-state-checks"))
                {
                    var packages = w.Packages;
                    var filter = (System.Windows.Controls.TextBox)packages.FindName("Filter");
                    filter.Text = "no-matching-package-ui-check";
                    await Shot("05-packages-no-results");
                    if (((FrameworkElement)packages.FindName("NoResults")).Visibility != Visibility.Visible)
                        throw new InvalidOperationException("The package library does not explain empty search results.");
                    ((System.Windows.Controls.Button)packages.FindName("BtnClearFilters")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    if (filter.Text.Length != 0 || ((System.Windows.Controls.ListBox)packages.FindName("List")).Items.Count == 0)
                        throw new InvalidOperationException("Clearing the package filters did not restore the results.");
                }
                w.Navigate("settings");
                await Shot("06-settings");
                foreach (var section in new[] { "appearance", "company", "defaults", "intune", "tools", "about" })
                {
                    w.Settings.ShowSection(section);
                    await Task.Delay(300);
                    await Shot("06-settings-" + section);
                }
                w.Settings.ShowSection("general");

                // The connection window, page by page (nothing is sent: the pages are only drawn).
                await ShotWindow(new Views.ConnectWindow(), "08-connect-choose", null);
                await ShotWindow(new Views.ConnectWindow(), "08-connect-shell", c => c.SnapshotPage("shell"));
                await ShotWindow(new Views.ConnectWindow(), "08-connect-progress", c => c.SnapshotPage("progress"));
                await ShotWindow(new Views.ConnectWindow(), "08-connect-failed", c => c.SnapshotPage("failed"));
                await ShotWindow(new Views.ConnectWindow(), "08-connect-done", c => c.SnapshotPage("done"));

                // Switches the language while everything is loaded, to check that every page follows it live.
                string relang = App.Arg("--snapshot-relang");
                if (!string.IsNullOrEmpty(relang))
                {
                    Loc.Instance.SetLanguage(Loc.Resolve(relang));
                    await Shot("07-relang-settings");
                    w.Navigate("home");
                    await Shot("07-relang-home");
                    w.Navigate("catalog");
                    await Shot("07-relang-catalog");
                    w.Navigate("editor");
                    foreach (int step in new[] { 1, 2, 3, 7 })
                    {
                        w.Editor.GoStep(step);
                        await Shot($"07-relang-editor-{step + 1}");
                    }
                    w.Navigate("packages");
                    await Shot("07-relang-packages");
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString());
            }
            Application.Current.Shutdown();
        }
    }
}
