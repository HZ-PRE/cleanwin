param(
    [string]$DotnetPath = 'dotnet',
    [string]$IsccPath = ''
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
New-Item -ItemType Directory -Force (Join-Path $root 'dist') | Out-Null
Start-Transcript -Path (Join-Path $root 'dist\full-test.log') -Force | Out-Null
try {
    foreach ($project in @('CleanOptimize.App', 'CleanOptimize.Core', 'SelfTestRunner')) {
        foreach ($leaf in @('bin', 'obj')) {
            $path = [IO.Path]::GetFullPath((Join-Path $root "src\$project\$leaf"))
            if (-not $path.StartsWith($root + '\src\', [StringComparison]::OrdinalIgnoreCase)) { throw '清理目录越界' }
            if (Test-Path -LiteralPath $path) {
                if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '构建目录不能是链接' }
                Remove-Item -LiteralPath $path -Recurse -Force
            }
        }
    }
    & (Join-Path $PSScriptRoot 'verify.ps1') -DotnetPath $DotnetPath -Configuration Debug
    & (Join-Path $PSScriptRoot 'verify.ps1') -DotnetPath $DotnetPath -Configuration Release
    & (Join-Path $PSScriptRoot 'publish.ps1') -DotnetPath $DotnetPath -IsccPath $IsccPath -RequireInstaller
    Write-Host '从零构建、Debug/Release 回归及发布产物自测全部完成。'
} finally { Stop-Transcript | Out-Null }
