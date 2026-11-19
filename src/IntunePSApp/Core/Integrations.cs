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

    /// <summary>Choices of one publication (the tenant and the client id are remembered in the settings).</summary>
    public class PublishOptions
    {
        public string TenantId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public bool DeviceCode { get; set; }
        /// <summary>Ask | Fail | Update | CreateNew</summary>
        public string IfExists { get; set; } = "Ask";
        /// <summary>none | available | required</summary>
        public string AssignIntent { get; set; } = "none";
        /// <summary>AllUsers | AllDevices | Group</summary>
        public string AssignTo { get; set; } = "AllUsers";
        public string GroupId { get; set; } = "";
        /// <summary>Intune app to update with this package (a previous version); empty: looked up by name.</summary>
        public string AppId { get; set; } = "";
    }

    /// <summary>Intune\publish-result.json, written by Publish-ToIntune.ps1 on every exit path.</summary>
    public class PublishResult
    {
        /// <summary>published | failed | cancelled</summary>
        public string Status { get; set; }
        /// <summary>created | updated</summary>
        public string Action { get; set; }
        public string AppId { get; set; }
        public string DisplayName { get; set; }
        public string TenantId { get; set; }
        public string PortalUrl { get; set; }
        public string Assignment { get; set; }
        public string Error { get; set; }
        public string Utc { get; set; }

        public bool IsPublished => string.Equals(Status, "published", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "Publish to Intune (preview)": runs the generated Publish-ToIntune.ps1 in a visible Windows PowerShell window.
    /// The script talks to Microsoft Graph with the administrator's own sign-in; this application never sees or
    /// stores credentials, and reads the outcome from the result file the script writes.
    /// </summary>
    public static class IntunePublisher
    {
        public const string ResultFileName = "publish-result.json";

        public static bool IsGuid(string value) => Guid.TryParse((value ?? "").Trim(), out _);

        /// <summary>Tenant: a GUID or a domain such as contoso.onmicrosoft.com.</summary>
        public static bool IsTenant(string value)
        {
            value = (value ?? "").Trim();
            return IsGuid(value) || Regex.IsMatch(value, @"^[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?)+$");
        }

        /// <summary>Null when the options can be sent; otherwise the key of the text that explains what is wrong.</summary>
        public static string Check(PublishOptions o)
        {
            if (!IsTenant(o.TenantId)) return "Publish_InvalidTenant";
            if (!IsGuid(o.ClientId)) return "Publish_InvalidClient";
            if (o.AssignIntent != "none")
            {
                if (o.AssignTo == "Group" && !IsGuid(o.GroupId)) return "Publish_InvalidGroup";
                // Intune has no "available" assignment for devices.
                if (o.AssignIntent == "available" && o.AssignTo == "AllDevices") return "Publish_InvalidAssignment";
            }
            return null;
        }

        /// <summary>Arguments of Windows PowerShell for one publication. Every value was validated by <see cref="Check"/>.</summary>
        public static string Arguments(string publishScript, PublishOptions o, bool pause = false)
        {
            var sb = new StringBuilder($"-NoProfile -ExecutionPolicy Bypass -File \"{publishScript}\" -TenantId \"{o.TenantId.Trim()}\" -ClientId \"{o.ClientId.Trim()}\"");
            if (o.DeviceCode) sb.Append(" -DeviceCode");
            if (o.IfExists is "Fail" or "Update" or "CreateNew") sb.Append(" -IfExists ").Append(o.IfExists);
            if (o.AssignIntent is "available" or "required")
            {
                sb.Append(" -AssignIntent ").Append(o.AssignIntent).Append(" -AssignTo ").Append(o.AssignTo is "AllDevices" or "Group" ? o.AssignTo : "AllUsers");
                if (o.AssignTo == "Group") sb.Append(" -GroupId ").Append(o.GroupId.Trim());
            }
            if (IsGuid(o.AppId)) sb.Append(" -AppId ").Append(Guid.Parse(o.AppId.Trim()).ToString());
            if (pause) sb.Append(" -Pause");
            // National clouds and test servers (the same variables the application uses for its own sign-in).
            if (Environment.GetEnvironmentVariable("J365_GRAPH_URL") is { Length: > 0 } graph && Uri.IsWellFormedUriString(graph, UriKind.Absolute)) sb.Append(" -GraphUrl \"").Append(graph).Append('"');
            if (Environment.GetEnvironmentVariable("J365_LOGIN_URL") is { Length: > 0 } login && Uri.IsWellFormedUriString(login, UriKind.Absolute)) sb.Append(" -LoginUrl \"").Append(login).Append('"');
            return sb.ToString();
        }

        /// <summary>Command line shown to the user (to copy and run by hand).</summary>
        public static string CommandLine(string publishScript, PublishOptions o) => "powershell.exe " + Arguments(publishScript, o);

        /// <summary>
        /// Writes the current publishing script into the package (packages generated by an older version carry a script
        /// that depends on a community module) and returns its path.
        /// </summary>
        public static string Prepare(string intuneFolder)
        {
            string script = Path.Combine(intuneFolder, PackageLayout.PublishScript);
            File.WriteAllText(script, PackageBuilder.CompanionScript(PackageLayout.PublishScript), Encoding.ASCII);
            return script;
        }

        /// <summary>Starts the publication in its own window. The caller waits for the process and reads the result file.</summary>
        public static Process Launch(string publishScript, PublishOptions o)
        {
            if (Check(o) != null) throw new ArgumentException("Invalid publishing options.");
            var psi = new ProcessStartInfo(ToolsService.WindowsPowerShell, Arguments(publishScript, o, pause: true))
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(publishScript)
            };
            // Windows PowerShell rebuilds its own module path (user + machine) when the variable is absent;
            // this drops PowerShell 7 paths inherited from a parent process.
            psi.Environment.Remove("PSModulePath");
            return Process.Start(psi);
        }

        /// <summary>
        /// Publishes inside the application: the script runs without a window, signs in with the session the user opened
        /// in the application (its refresh token goes in an environment variable of that process only, never on the
        /// command line), answers no question (<c>-Force</c>, so <paramref name="o"/> must say what to do with an app of
        /// the same name) and reports every line it prints. The caller waits for the process and reads the result file.
        /// </summary>
        /// <summary>The file whose creation asks a running in-app publication to stop (the script then cleans up).</summary>
        public static string CancelFile(string publishScript) => Path.Combine(Path.GetDirectoryName(publishScript) ?? "", "publish-cancel.flag");

        public static Process LaunchInApp(string publishScript, PublishOptions o, string refreshToken, Action<string> output)
        {
            if (Check(o) != null || o.IfExists is not ("Update" or "CreateNew" or "Fail")) throw new ArgumentException("Invalid publishing options.");
            string cancel = CancelFile(publishScript);
            try { if (File.Exists(cancel)) File.Delete(cancel); } catch { }
            var psi = ToolsService.PowerShellStartInfo(Arguments(publishScript, o) + " -Force -CancelFile \"" + cancel + "\"");
            psi.WorkingDirectory = Path.GetDirectoryName(publishScript);
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.Environment.Remove("PSModulePath");
            psi.Environment["J365_GRAPH_REFRESH_TOKEN"] = refreshToken ?? "";
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) output?.Invoke(e.Data.TrimEnd()); };
            p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) output?.Invoke(e.Data.TrimEnd()); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        /// <summary>Checks the package the way the publication does, without signing in. Returns the text the script printed.</summary>
        public static (bool Ok, string Output) Validate(string publishScript)
        {
            var psi = ToolsService.PowerShellStartInfo($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{publishScript}\" -ValidateOnly");
            psi.WorkingDirectory = Path.GetDirectoryName(publishScript);
            var run = ToolsService.RunPowerShell(psi, 120000);
            return (!run.TimedOut && run.ExitCode == 0, (run.Output ?? "").Trim());
        }

        public static string ResultFile(string intuneFolder) => Path.Combine(intuneFolder, ResultFileName);

        /// <summary>Outcome of the last publication of a package; null when it was never published from here.</summary>
        public static PublishResult ReadResult(string intuneFolder)
        {
            try
            {
                string file = ResultFile(intuneFolder);
                if (!File.Exists(file)) return null;
                return JsonSerializer.Deserialize<PublishResult>(File.ReadAllText(file, Encoding.UTF8), Json.Options);
            }
            catch { return null; }
        }
    }
}
