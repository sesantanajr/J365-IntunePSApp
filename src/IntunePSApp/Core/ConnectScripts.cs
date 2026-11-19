namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// The same app registration as the automatic setup, created with the Azure CLI of Azure Cloud Shell
    /// (shell.azure.com). It is the way out when the tenant does not let "Microsoft Graph Command Line Tools" ask for
    /// the permission to create applications: the Azure CLI is already authorized in every tenant, and an administrator
    /// signed in to the portal runs it without any consent screen. The last line printed is a connection code
    /// ("J365-CONNECT tenant=... client=...") that the application reads.
    /// </summary>
    public static class ConnectScripts
    {
        /// <summary>For the Bash prompt of Cloud Shell (the default).</summary>
        public const string Bash =
@"(
G=00000003-0000-0000-c000-000000000000
APP=$(az ad app list --display-name ""J365 IntunePSApp"" --query ""[0].appId"" -o tsv)
[ -z ""$APP"" ] && APP=$(az ad app create --display-name ""J365 IntunePSApp"" --sign-in-audience AzureADMyOrg --query appId -o tsv)
az ad app update --id ""$APP"" --is-fallback-public-client true --public-client-redirect-uris http://localhost
SCOPE=$(az ad sp show --id $G --query ""oauth2PermissionScopes[?value=='DeviceManagementApps.ReadWrite.All'].id | [0]"" -o tsv)
az ad app permission add --id ""$APP"" --api $G --api-permissions ""$SCOPE=Scope"" >/dev/null 2>&1
for i in 1 2 3 4 5 6 7 8; do az ad sp show --id ""$APP"" >/dev/null 2>&1 && break; az ad sp create --id ""$APP"" >/dev/null 2>&1 && break; sleep 5; done
az ad app permission grant --id ""$APP"" --api $G --scope DeviceManagementApps.ReadWrite.All >/dev/null 2>&1
T=$(az rest --method get --url https://graph.microsoft.com/v1.0/organization --query ""value[0].id"" -o tsv)
echo ""J365-CONNECT tenant=$T client=$APP""
)";

        /// <summary>For the PowerShell prompt of Cloud Shell.</summary>
        public const string PowerShell =
@"& {
$G = '00000003-0000-0000-c000-000000000000'
$app = az ad app list --display-name 'J365 IntunePSApp' --query '[0].appId' -o tsv
if (-not $app) { $app = az ad app create --display-name 'J365 IntunePSApp' --sign-in-audience AzureADMyOrg --query appId -o tsv }
az ad app update --id $app --is-fallback-public-client true --public-client-redirect-uris http://localhost
$scope = az ad sp show --id $G --query ""oauth2PermissionScopes[?value=='DeviceManagementApps.ReadWrite.All'].id | [0]"" -o tsv
az ad app permission add --id $app --api $G --api-permissions ""$scope=Scope"" *> $null
foreach ($i in 1..8) { az ad sp show --id $app *> $null; if ($LASTEXITCODE -eq 0) { break }; az ad sp create --id $app *> $null; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep 5 }
az ad app permission grant --id $app --api $G --scope DeviceManagementApps.ReadWrite.All *> $null
$t = az rest --method get --url https://graph.microsoft.com/v1.0/organization --query 'value[0].id' -o tsv
""J365-CONNECT tenant=$t client=$app""
}";
    }
}
