using System;
using System.IO;
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
                }
                if (App.HasArg("--snapshot-build"))
                {
                    var s = w.Editor.Collect();
                    string outDir = App.Arg("--snapshot-out");
                    if (!string.IsNullOrEmpty(outDir)) s.OutputRoot = outDir;
                    await w.Editor.BuildAsync(s);
                    await Shot("04-generated");
                    if (!string.IsNullOrEmpty(outDir)) ConfigStore.Current.OutputFolder = outDir;
                }

                w.Navigate("packages");
                await Shot("05-packages");
                w.Navigate("settings");
                await Shot("06-settings");
                w.Settings.ShowIntune();
                await Task.Delay(300);
                await Shot("06-settings-intune");
                w.Settings.ScrollToEnd();
                await Shot("06-settings-end");

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
