using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using J365.IntunePSApp.Core;

namespace J365.IntunePSApp
{
    /// <summary>
    /// Modo de captura (testes visuais e imagens da documentacao):
    ///   J365.IntunePSApp.exe --snapshot <pasta> [--snapshot-installer <arquivo>] [--snapshot-build] [--snapshot-out <pasta>]
    /// </summary>
    public static class SnapshotRunner
    {
        public static async Task RunAsync(MainWindow w, string dir)
        {
            Directory.CreateDirectory(dir);
            try
            {
                async Task Shot(string name)
                {
                    await Task.Delay(500);
                    w.UpdateLayout();
                    var root = (FrameworkElement)w.Content;
                    var rtb = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(root);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using var fs = File.Create(Path.Combine(dir, name + ".png"));
                    enc.Save(fs);
                }

                w.Navigate("home");
                await Shot("01-inicio");

                w.Navigate("catalog");
                w.Catalog.FocusSearch(App.Arg("--snapshot-search") ?? "7-Zip");
                await Task.Delay(900);
                await Shot("02-catalogo");

                w.Navigate("editor");
                string inst = App.Arg("--snapshot-installer");
                if (!string.IsNullOrEmpty(inst) && File.Exists(inst))
                {
                    await w.Editor.AnalyzeFileAsync(inst);
                    var s = w.Editor.Collect();
                    if (string.IsNullOrEmpty(s.CompanyName)) s.CompanyName = "Contoso Ltda";
                    if (string.IsNullOrEmpty(s.WelcomeMessage)) s.WelcomeMessage = $"O {s.AppName} será atualizado para a versão {s.AppVersion}. Salve seus arquivos antes de continuar.";
                    if (string.IsNullOrEmpty(s.CompletionMessage)) s.CompletionMessage = $"Pronto! O {s.AppName} foi atualizado com sucesso.";
                    if (s.Actions.Count == 0)
                    {
                        s.Actions.Add(new PackageAction { Type = "RemoveDesktopShortcut", Phase = "PostInstall", Params = { ["Name"] = s.AppName + "*" } });
                        s.Actions.Add(new PackageAction { Type = "SetRegistry", Phase = "PostInstall", Params = { ["Key"] = @"HKLM:\SOFTWARE\Contoso\Inventario", ["Name"] = s.AppName, ["Value"] = s.AppVersion, ["Type"] = "String" } });
                    }
                    w.Editor.LoadSettings(s);
                }
                for (int i = 0; i < w.Editor.StepCount; i++)
                {
                    w.Editor.GoStep(i);
                    await Shot($"03-editor-{i + 1}");
                }
                if (App.HasArg("--snapshot-build"))
                {
                    var s = w.Editor.Collect();
                    string outDir = App.Arg("--snapshot-out");
                    if (!string.IsNullOrEmpty(outDir)) s.OutputRoot = outDir;
                    await w.Editor.BuildAsync(s);
                    await Shot("04-gerado");
                    if (!string.IsNullOrEmpty(outDir)) { ConfigStore.Current.OutputFolder = outDir; }
                }

                w.Navigate("packages");
                await Shot("05-pacotes");
                w.Navigate("settings");
                await Shot("06-configuracoes");
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(dir, "erro.txt"), ex.ToString());
            }
            Application.Current.Shutdown();
        }
    }
}
