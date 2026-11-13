param(
    [string]$DotnetPath = "dotnet",
    [ValidateSet('Debug', 'Release')][string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
& (Join-Path $PSScriptRoot 'security-check.ps1')

& $DotnetPath build (Join-Path $root "src\CleanOptimize.App\CleanOptimize.App.csproj") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "应用构建失败" }
& $DotnetPath run --project (Join-Path $root "src\SelfTestRunner\SelfTestRunner.csproj") -c $Configuration --no-launch-profile |
    Tee-Object -FilePath (Join-Path $outDir "verify-$Configuration.log")
if ($LASTEXITCODE -ne 0) { throw "安全及回归测试失败" }
Write-Host "构建和安全回归测试通过 ($Configuration)"
