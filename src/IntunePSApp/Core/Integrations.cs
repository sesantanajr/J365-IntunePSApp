using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace J365.IntunePSApp.Core
{
    public enum SandboxState
    {
        /// <summary>The feature is enabled and no sandbox is open.</summary>
        Ready,
        /// <summary>A sandbox is already open (Windows allows a single instance).</summary>
        Running,
        /// <summary>This edition of Windows has the feature, but it is turned off.</summary>
        NotEnabled,
        /// <summary>Windows Home or Windows Server: the feature does not exist.</summary>
        NotSupported
    }

    /// <summary>One run recorded by Sandbox-Test.ps1 (install or uninstall followed by the detection rule).</summary>
    public class SandboxStep
    {
        public string Step { get; set; }
        public bool Passed { get; set; }
        public int? ExitCode { get; set; }
        public bool Detected { get; set; }
        public int Seconds { get; set; }
    }

    /// <summary>Sandbox\Results\result.json, written inside the sandbox through a writable mapped folder.</summary>
    public class SandboxResult
    {
        public string App { get; set; }
        public bool Passed { get; set; }
        public string LastStep { get; set; }
        public bool LastPassed { get; set; }
        public string UpdatedUtc { get; set; }
        public List<SandboxStep> Steps { get; set; } = new List<SandboxStep>();

        public SandboxStep Last => Steps != null && Steps.Count > 0 ? Steps[^1] : null;
    }

    /// <summary>
    /// Windows Sandbox integration: a disposable Windows where the package can be installed for real
    /// without touching the packaging computer.
    /// </summary>
    public static class SandboxService
    {
        /// <summary>Name of the optional Windows feature ("Windows Sandbox" / "Área Restrita do Windows").</summary>
        public const string FeatureName = "Containers-DisposableClientVM";

        private const string SandboxRoot = @"C:\J365Package";
        private const string SandboxResultsRoot = @"C:\J365Results";
        private const string ResultsDirName = "Results";
        private const string ResultFileName = "result.json";

        // Processes that only exist while a sandbox is open: the window of the classic sandbox (Client), the window and the
        // virtual machine of the current one (RemoteSession, vmmemWindowsSandbox). WindowsSandboxServer is not in the list:
        // it is a background service started by the "wsb" command line and stays up with no sandbox open.
        private static readonly string[] ProcessNames = { "WindowsSandbox", "WindowsSandboxClient", "WindowsSandboxRemoteSession", "vmmemWindowsSandbox" };

        private static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);

        public static string SandboxExe => Path.Combine(System32, "WindowsSandbox.exe");

        /// <summary>True when the optional Windows feature "Windows Sandbox" is enabled on this computer.</summary>
        public static bool IsAvailable => File.Exists(SandboxExe);

        /// <summary>True while a sandbox is open: starting a second one fails with a Windows error.</summary>
        public static bool IsRunning
        {
            get
            {
                foreach (var name in ProcessNames)
                {
                    Process[] found;
                    try { found = Process.GetProcessesByName(name); } catch { continue; }
                    foreach (var p in found) p.Dispose();
                    if (found.Length > 0) return true;
                }
                return false;
            }
        }

        public static SandboxState State
            => IsAvailable ? (IsRunning ? SandboxState.Running : SandboxState.Ready)
             : IsSupportedEdition(WindowsEdition, WindowsInstallationType) ? SandboxState.NotEnabled : SandboxState.NotSupported;

        private static string CurrentVersionValue(string name)
        {
            try
            {
                using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                return key?.GetValue(name) as string ?? "";
            }
            catch { return ""; }
        }

        public static string WindowsEdition => CurrentVersionValue("EditionID");
        private static string WindowsInstallationType => CurrentVersionValue("InstallationType");

        /// <summary>Windows Sandbox exists in Pro, Enterprise and Education; not in Home ("Core*") nor in Windows Server.</summary>
        public static bool IsSupportedEdition(string editionId, string installationType)
        {
            if (!string.IsNullOrEmpty(installationType) && !installationType.Equals("Client", StringComparison.OrdinalIgnoreCase)) return false;
            return !(editionId ?? "").StartsWith("Core", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when hardware virtualization looks disabled in the firmware (BIOS/UEFI). Only meaningful while no
        /// hypervisor is running: under Hyper-V the processor no longer reports the firmware switch.
        /// </summary>
        public static bool VirtualizationLooksDisabled
        {
            get
            {
                try
                {
                    if (System.Runtime.Intrinsics.X86.X86Base.IsSupported && (System.Runtime.Intrinsics.X86.X86Base.CpuId(1, 0).Ecx & (1 << 31)) != 0) return false;
                    return !NativeMethods.IsProcessorFeaturePresent(NativeMethods.PF_VIRT_FIRMWARE_ENABLED);
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Turns the Windows feature on with DISM, elevated (Windows shows the UAC prompt). Returns DISM's exit code:
        /// 0 = enabled, 3010 = enabled and a restart is required. Throws when the elevation is refused.
        /// </summary>
        public static async Task<int> EnableFeatureAsync()
        {
            var psi = new ProcessStartInfo(Path.Combine(System32, "dism.exe"), $"/Online /Enable-Feature /FeatureName:{FeatureName} /All /NoRestart")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("dism.exe");
            await p.WaitForExitAsync();
            return p.ExitCode;
        }

        public static string ResultsDir(string packageFolder) => Path.Combine(packageFolder, PackageLayout.SandboxDir, ResultsDirName);
        public static string ResultFile(string packageFolder) => Path.Combine(ResultsDir(packageFolder), ResultFileName);

        /// <summary>The result written by the test inside the sandbox; null while there is none (or it is being written).</summary>
        public static SandboxResult ReadResult(string packageFolder)
        {
            try
            {
                string file = ResultFile(packageFolder);
                if (!File.Exists(file)) return null;
                var result = JsonSerializer.Deserialize<SandboxResult>(File.ReadAllText(file, Encoding.UTF8), Json.Options);
                if (result != null) result.Steps ??= new List<SandboxStep>();
                return result;
            }
            catch { return null; } // being written by the sandbox right now: read again on the next tick
        }

        /// <summary>True when the folder holds a package this version can test (its intune-app.json describes the commands).</summary>
        public static bool CanTest(string packageFolder)
            => File.Exists(Path.Combine(packageFolder, PackageLayout.IntuneDir, PackageLayout.MetadataFile))
               && PackageLayout.FindPackageDir(packageFolder) == Path.Combine(packageFolder, PackageLayout.PackageDir);

        /// <summary>
        /// Gets a package ready for the test and returns its .wsb file. The test script is written again, so packages
        /// generated by an older version also get the current one (progress, time limit, result on the host).
        /// </summary>
        public static string Prepare(string packageFolder)
        {
            string dir = Path.Combine(packageFolder, PackageLayout.SandboxDir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, PackageLayout.SandboxScript), PackageBuilder.CompanionScript(PackageLayout.SandboxScript), Encoding.ASCII);
            return CreateConfig(packageFolder);
        }

        /// <summary>
        /// Writes the .wsb file: the package is mapped read-only, Sandbox\Results is mapped writable (result and logs
        /// come back to the host) and the test script starts at logon. The result of a previous test is removed.
        /// </summary>
        public static string CreateConfig(string packageFolder)
        {
            string root = Path.GetFullPath(packageFolder).TrimEnd('\\');
            string dir = Path.Combine(root, PackageLayout.SandboxDir);
            string results = ResultsDir(root);
            Directory.CreateDirectory(results);
            try { File.Delete(ResultFile(root)); } catch { }

            string script = $@"{SandboxRoot}\{PackageLayout.SandboxDir}\{PackageLayout.SandboxScript}";
            // QuickEdit off for the console about to open: with it, one click on the window pauses the whole test.
            string command = "cmd.exe /c reg add HKCU\\Console /v QuickEdit /t REG_DWORD /d 0 /f >nul 2>&1 & " +
                             $"start \"{AppInfo.Name}\" powershell.exe -NoExit -ExecutionPolicy Bypass -File \"{script}\"";
            string xml =
                "<Configuration>\r\n" +
                "  <Networking>Enable</Networking>\r\n" +
                "  <MappedFolders>\r\n" +
                "    <MappedFolder>\r\n" +
                $"      <HostFolder>{SecurityElement.Escape(root)}</HostFolder>\r\n" +
                $"      <SandboxFolder>{SandboxRoot}</SandboxFolder>\r\n" +
                "      <ReadOnly>true</ReadOnly>\r\n" +
                "    </MappedFolder>\r\n" +
                "    <MappedFolder>\r\n" +
                $"      <HostFolder>{SecurityElement.Escape(results)}</HostFolder>\r\n" +
                $"      <SandboxFolder>{SandboxResultsRoot}</SandboxFolder>\r\n" +
                "      <ReadOnly>false</ReadOnly>\r\n" +
                "    </MappedFolder>\r\n" +
                "  </MappedFolders>\r\n" +
                "  <LogonCommand>\r\n" +
                $"    <Command>{SecurityElement.Escape(command)}</Command>\r\n" +
                "  </LogonCommand>\r\n" +
                "</Configuration>\r\n";
            string wsb = Path.Combine(dir, Names.Safe(Path.GetFileName(root)) + ".wsb");
            File.WriteAllText(wsb, xml, new UTF8Encoding(false));
            return wsb;
        }

        public static void Launch(string wsbFile) => Process.Start(new ProcessStartInfo(wsbFile) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>
    /// "Publish to Intune (preview)": runs the generated Publish-ToIntune.ps1 in a visible Windows PowerShell window.
    /// The script uses the community IntuneWin32App module and the administrator's own sign-in; this application
    /// never sees or stores credentials.
    /// </summary>
    public static class IntunePublisher
    {
        public static bool IsGuid(string value) => Guid.TryParse((value ?? "").Trim(), out _);

        /// <summary>Tenant: a GUID or a domain such as contoso.onmicrosoft.com.</summary>
        public static bool IsTenant(string value)
        {
            value = (value ?? "").Trim();
            return IsGuid(value) || Regex.IsMatch(value, @"^[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?)+$");
        }

        /// <summary>Command line shown to the user and used to start the publication.</summary>
        public static string Arguments(string publishScript, string tenantId, string clientId, bool deviceCode)
            => $"-NoExit -ExecutionPolicy Bypass -File \"{publishScript}\" -TenantId \"{tenantId.Trim()}\" -ClientId \"{clientId.Trim()}\"" + (deviceCode ? " -DeviceCode" : "");

        public static string CommandLine(string publishScript, string tenantId, string clientId, bool deviceCode)
            => "powershell.exe " + Arguments(publishScript, tenantId, clientId, deviceCode);

        public static void Launch(string publishScript, string tenantId, string clientId, bool deviceCode)
        {
            if (!IsTenant(tenantId) || !IsGuid(clientId)) throw new ArgumentException("Invalid tenant or client id.");
            var psi = new ProcessStartInfo(ToolsService.WindowsPowerShell, Arguments(publishScript, tenantId, clientId, deviceCode))
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(publishScript)
            };
            // Windows PowerShell rebuilds its own module path (user + machine) when the variable is absent;
            // this drops PowerShell 7 paths inherited from a parent process.
            psi.Environment.Remove("PSModulePath");
            Process.Start(psi);
        }
    }
}
