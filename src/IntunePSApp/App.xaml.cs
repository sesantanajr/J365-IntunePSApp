using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using J365.IntunePSApp.Core;

namespace J365.IntunePSApp
{
    public partial class App : Application
    {
        public static string[] Args { get; private set; } = Array.Empty<string>();

        public static string Arg(string name)
        {
            int i = Array.FindIndex(Args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null;
        }

        public static bool HasArg(string name) => Args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        protected override void OnStartup(StartupEventArgs e)
        {
            Args = e.Args ?? Array.Empty<string>();
            DispatcherUnhandledException += OnUiException;
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log.Error("Erro não tratado: " + ex.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, ex) => { Log.Error("Erro em tarefa: " + ex.Exception.GetBaseException().Message); ex.SetObserved(); };

            base.OnStartup(e);

            // Modo linha de comando: J365.IntunePSApp.exe --build <projeto.json | instalador.msi/.exe> [--out pasta] [--native]
            if (HasArg("--build"))
            {
                int code = RunCli();
                Shutdown(code);
                return;
            }

            Log.Info($"{AppInfo.Name} {AppInfo.Version} iniciado. Dados: {AppPaths.Data}");
            var w = new MainWindow();
            MainWindow = w;
            if (HasArg("--snapshot"))
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = -32000; w.Top = 0;
                w.ShowActivated = false;
                w.ShowInTaskbar = false;
            }
            w.Show();
        }

        private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Erro na interface: " + e.Exception);
            e.Handled = true;
            try
            {
                MessageBox.Show(MainWindow, "Ocorreu um erro inesperado, mas o aplicativo continua funcionando.\n\n" + e.Exception.GetBaseException().Message +
                    $"\n\nDetalhes em: {AppPaths.Logs}", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch { }
        }

        // Executa fora do contexto da interface para evitar deadlock entre await e espera sincrona.
        private static int RunCli() => Task.Run(RunCliCore).GetAwaiter().GetResult();

        private static int RunCliCore()
        {
            NativeMethods.AttachConsole(-1);
            Log.Entry += en => Console.WriteLine($"[{en.TimeLabel}] {en.Text}");
            try
            {
                string input = Arg("--build");
                if (string.IsNullOrWhiteSpace(input) || !File.Exists(input)) { Console.WriteLine("Uso: J365.IntunePSApp.exe --build <projeto.json|instalador> [--out pasta] [--native]"); return 2; }
                CatalogService.Instance.LoadAsync().GetAwaiter().GetResult();
                PackageSettings s;
                if (input.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) s = Json.Load<PackageSettings>(input);
                else
                {
                    s = ConfigStore.Current.NewPackage();
                    var info = InstallerAnalyzer.Analyze(input);
                    InstallerAnalyzer.ApplyTo(s, info);
                    foreach (var n in info.Notes) Console.WriteLine("  - " + n);
                }
                var outDir = Arg("--out");
                if (!string.IsNullOrWhiteSpace(outDir)) s.OutputRoot = outDir;
                var r = PackageBuilder.BuildAsync(s, HasArg("--native"), CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine();
                Console.WriteLine("Pacote:     " + r.PackageFolder);
                Console.WriteLine("intunewin:  " + r.IntuneWinFile);
                Console.WriteLine("Deteccao:   " + r.DetectionScript);
                Console.WriteLine("Instalacao: " + r.InstallCommand);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERRO: " + ex.Message);
                return 1;
            }
        }
    }
}
