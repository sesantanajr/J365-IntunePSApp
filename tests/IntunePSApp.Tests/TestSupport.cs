using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

// The language is process-wide state: tests that switch it must not run side by side.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace J365.IntunePSApp.Tests
{
    internal static class TestSetup
    {
        public static readonly string DataDir = Path.Combine(Path.GetTempPath(), "j365-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        /// <summary>Keeps the application's data folder (logs, temp) out of the build output and of the user profile.</summary>
        [ModuleInitializer]
        public static void Init()
        {
            Environment.SetEnvironmentVariable("J365_INTUNEPSAPP_DATA", DataDir);
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { Directory.Delete(DataDir, true); } catch { } };
        }
    }

    /// <summary>A temporary folder removed at the end of the test.</summary>
    public sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "j365-t-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        public TempFolder() { Directory.CreateDirectory(Path); }
        public string File(string name, string content = "")
        {
            string p = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
            System.IO.File.WriteAllText(p, content, new UTF8Encoding(false));
            return p;
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }

    internal static class Ps
    {
        /// <summary>Syntax errors reported by the Windows PowerShell 5.1 parser for a script text (nothing is executed).</summary>
        public static string[] SyntaxErrors(string script)
        {
            using var tmp = new TempFolder();
            string file = Path.Combine(tmp.Path, "script.ps1");
            File.WriteAllText(file, script, new UTF8Encoding(true));
            return PowerShellValidator.Validate(file);
        }

        public static void AssertParses(string script) => Assert.True(SyntaxErrors(script).Length == 0, "Syntax errors:\n" + string.Join("\n", SyntaxErrors(script)));

        public static bool IsAscii(string text) => text.All(c => c < 128);
    }

    internal static class Lang
    {
        /// <summary>Runs an action with a given interface language and restores the previous one.</summary>
        public static void With(string code, Action action)
        {
            string before = Loc.Instance.Language;
            Loc.Instance.SetLanguage(code);
            try { action(); }
            finally { Loc.Instance.SetLanguage(before); }
        }
    }

    internal static class Repo
    {
        /// <summary>Repository root (the folder that holds the solution file), found from the test output folder.</summary>
        public static string Root
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("J365_REPO_ROOT"); // for test runs with an output folder outside the repository
                if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "J365-IntunePSApp.slnx"))) dir = dir.Parent;
                return dir?.FullName;
            }
        }
    }

    internal static class Fakes
    {
        /// <summary>Smallest text with the structure the generator expects from the PSAppDeployToolkit v4 template.</summary>
        public const string Template = @"[CmdletBinding()]
param
(
    [System.String]$DeploymentType = 'Install',
    [System.String]$DeployMode = 'Auto'
)

##================================================
## MARK: Variables
##================================================

$adtSession = @{
    AppVendor = ''
    AppName = ''
    DeployAppScriptVersion = '4.1.8'
}

function Install-ADTDeployment
{
}

##================================================
## MARK: Initialization
##================================================

$ErrorActionPreference = 'Stop'
& ""$($DeploymentType)-ADTDeployment""
";

        public const string Config = @"@{
    Toolkit = @{
        CompanyName = 'PSAppDeployToolkit'
        LogPath = '$envWinDir\Logs\Software'
    }
    UI = @{
        DialogStyle = 'Fluent'
        FluentAccentColor = $null
        LanguageOverride = $null
    }
}
";

        public const string Strings = @"@{
    CloseAppsPrompt = @{
        Fluent = @{
            DialogMessage = 'Please save your work.'
        }
        CustomMessage = 'This is a custom message that can be added'
    }
    DeferPrompt = @{
        CustomMessage = 'Another custom message'
    }
}
";

        public static PackageSettings Settings(string type = "EXE") => new PackageSettings
        {
            InstallerPath = "",
            InstallerType = type,
            AppName = "Contoso App",
            AppVendor = "Contoso",
            AppVersion = "1.2.3",
            AppArch = "x64",
            InstallArgs = type == "EXE" ? "/S" : "",
            UninstallPattern = "Contoso App*",
            UninstallExeArgs = "/S",
            DetectionNamePattern = "Contoso App*",
            DetectionVersion = "1.2.3",
            ProcessesToClose = { "contoso", "contosohelper" },
            MinimumOs = "W10_22H2",
        };
    }
}
