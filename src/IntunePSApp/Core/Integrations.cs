using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// Windows Sandbox integration: a disposable Windows where the package can be installed for real
    /// without touching the packaging computer.
    /// </summary>
    public static class SandboxService
    {
        private const string SandboxRoot = @"C:\J365Package";

        public static string SandboxExe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsSandbox.exe");

        /// <summary>True when the optional Windows feature "Windows Sandbox" is enabled on this computer.</summary>
        public static bool IsAvailable => File.Exists(SandboxExe);

        /// <summary>Writes the .wsb file that maps the package (read-only) and starts the test script at logon.</summary>
        public static string CreateConfig(string packageFolder)
        {
            string dir = Path.Combine(packageFolder, PackageLayout.SandboxDir);
            Directory.CreateDirectory(dir);
            string script = $@"{SandboxRoot}\{PackageLayout.SandboxDir}\{PackageLayout.SandboxScript}";
            string xml =
                "<Configuration>\r\n" +
                "  <Networking>Enable</Networking>\r\n" +
                "  <MappedFolders>\r\n" +
                "    <MappedFolder>\r\n" +
                $"      <HostFolder>{SecurityElement.Escape(Path.GetFullPath(packageFolder).TrimEnd('\\'))}</HostFolder>\r\n" +
                $"      <SandboxFolder>{SandboxRoot}</SandboxFolder>\r\n" +
                "      <ReadOnly>true</ReadOnly>\r\n" +
                "    </MappedFolder>\r\n" +
                "  </MappedFolders>\r\n" +
                "  <LogonCommand>\r\n" +
                $"    <Command>cmd.exe /c start \"J365 IntunePSApp\" powershell.exe -NoExit -ExecutionPolicy Bypass -File \"{script}\"</Command>\r\n" +
                "  </LogonCommand>\r\n" +
                "</Configuration>\r\n";
            string wsb = Path.Combine(dir, Names.Safe(Path.GetFileName(packageFolder.TrimEnd('\\'))) + ".wsb");
            File.WriteAllText(wsb, xml, new UTF8Encoding(false));
            return wsb;
        }

        public static void Launch(string wsbFile) => Process.Start(new ProcessStartInfo(wsbFile) { UseShellExecute = true });
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
