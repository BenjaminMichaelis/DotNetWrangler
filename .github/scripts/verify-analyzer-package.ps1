param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,
    [Parameter(Mandatory)]
    [string] $PackageVersion,
    [string[]] $SdkVersions = @("10.0.100", "11.0.100")
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$packageSource = (Resolve-Path $PackageDirectory).Path
$consumerRoot = Join-Path ([IO.Path]::GetTempPath()) ("wrangler-package-consumer-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $consumerRoot | Out-Null

try {
    foreach ($sdkVersion in $SdkVersions) {
        $consumer = Join-Path $consumerRoot $sdkVersion
        New-Item -ItemType Directory -Path $consumer | Out-Null
        @{ sdk = @{ version = $sdkVersion; rollForward = "latestPatch"; allowPrerelease = $false } } |
            ConvertTo-Json -Depth 3 | Set-Content (Join-Path $consumer "global.json")

        $escapedSource = [System.Security.SecurityElement]::Escape($packageSource)
        $escapedVersion = [System.Security.SecurityElement]::Escape($PackageVersion)
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <RestoreAdditionalProjectSources>$escapedSource</RestoreAdditionalProjectSources>
    <RestorePackagesPath>packages</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DotNetWrangler.Analyzers" Version="$escapedVersion" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $consumer "Consumer.csproj")
        @'
root = true
[*.cs]
dotnet_diagnostic.WRANGLER0001.severity = error
dotnet_diagnostic.WRANGLER0002.severity = error
dotnet_diagnostic.WRANGLER0004.severity = error
dotnet_diagnostic.WRANGLER0005.severity = error
'@ | Set-Content (Join-Path $consumer ".editorconfig")
        @'
public static class Consumer
{
    public static string GetValue(string? value) => value!;
    public static string GetNull() => null!;
#pragma warning disable CS0168
    public static void Unused() { int unused; }
#pragma warning restore CS0168
}
'@ | Set-Content (Join-Path $consumer "Consumer.cs")

        Push-Location $consumer
        try {
            $selectedSdk = & dotnet --version
            if ($LASTEXITCODE -ne 0 -or $selectedSdk -notlike "$($sdkVersion.Substring(0, $sdkVersion.LastIndexOf('.'))).*") {
                throw "Could not select SDK $sdkVersion; selected '$selectedSdk'."
            }
            Write-Output "Verifying packaged analyzer with SDK $selectedSdk (net8.0 consumer)."
            $output = & dotnet build Consumer.csproj --configuration Release --verbosity minimal -warnaserror 2>&1
            $exitCode = $LASTEXITCODE
            $log = $output -join [Environment]::NewLine
            $log | Write-Output

            if ($log -match "\b(AD0001|CS8032|CS8034|CS8040|CS9057)\b") {
                throw "The packaged analyzer failed to load or threw an exception with SDK $selectedSdk."
            }
            if ($exitCode -eq 0) {
                throw "The packaged analyzer did not fail the consumer build with SDK $selectedSdk."
            }
            foreach ($id in @("WRANGLER0001", "WRANGLER0002", "WRANGLER0004", "WRANGLER0005")) {
                if ($log -notmatch "error ${id}:") {
                    throw "The packaged analyzer did not report $id with SDK $selectedSdk."
                }
            }

            $configuration = Join-Path $consumer ".editorconfig"
            (Get-Content $configuration -Raw).Replace("severity = error", "severity = none") |
                Set-Content $configuration
            $output = & dotnet build Consumer.csproj --configuration Release --verbosity minimal -warnaserror --no-restore 2>&1
            if ($LASTEXITCODE -ne 0) {
                $output | Write-Output
                throw "The consumer failed to build with Wrangler disabled under SDK $selectedSdk."
            }
            Write-Output "Package diagnostics and severity configuration verified with SDK $selectedSdk."
        }
        finally {
            Pop-Location
        }
    }
}
finally {
    Remove-Item $consumerRoot -Recurse -Force
}
