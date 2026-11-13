param([string]$Version = '')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if (-not $Version) {
    [xml]$project = Get-Content (Join-Path $root 'src\CleanOptimize.App\CleanOptimize.App.csproj') -Raw
    $Version = [string]$project.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw '版本号无效' }
$dist = Join-Path $root 'dist'
$zipDir = Join-Path $dist ('zip-smoke-' + [Guid]::NewGuid().ToString('N'))
$installDir = Join-Path $dist ('install-smoke-' + [Guid]::NewGuid().ToString('N'))
foreach ($path in @($zipDir, $installDir)) {
    if (-not [IO.Path]::GetFullPath($path).StartsWith([IO.Path]::GetFullPath($dist) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '验收目录越界' }
    if (Test-Path -LiteralPath $path) { throw '验收目录必须是新目录' }
}
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{47D73EFF-277F-4B0E-B37E-4902F848128A}_is1'
foreach ($hive in @('HKCU:', 'HKLM:', 'HKLM:\Software\WOW6432Node')) {
    $key = if ($hive -like '*WOW6432Node') { "$hive\Microsoft\Windows\CurrentVersion\Uninstall\{47D73EFF-277F-4B0E-B37E-4902F848128A}_is1" } else { "$hive\$uninstallKey" }
    if (Test-Path $key) { throw '发现已有安装，停止安装验收以避免覆盖' }
}
function Run-CheckedProcess([string]$Exe, [string[]]$Arguments, [int]$Timeout = 180000) {
    $process = Start-Process -FilePath $Exe -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit($Timeout)) { $process.Kill(); throw "进程超时: $Exe" }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "进程失败: $Exe (exit $($process.ExitCode))" }
}
# 不依赖本地安装的 SDK/运行时。
Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue
Remove-Item Env:DOTNET_ROOT_X64 -ErrorAction SilentlyContinue
Expand-Archive -LiteralPath (Join-Path $dist "CleanOptimize-$Version-win-x64.zip") -DestinationPath $zipDir
Run-CheckedProcess (Join-Path $zipDir 'CleanOptimize.exe') @('--selftest', '--selftest-log', ('"' + (Join-Path $dist 'zip-selftest.log') + '"'))
Write-Host 'ZIP 解压后独立运行、自测通过。'
$setup = Join-Path $dist "CleanOptimizeSetup-$Version.exe"
try {
    Run-CheckedProcess $setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/NOICONS',
        '/MERGETASKS=!desktopicon,!autostart', ('/DIR="' + $installDir + '"'), ('/LOG="' + (Join-Path $dist 'install-smoke.log') + '"'))
    if (-not (Test-Path (Join-Path $installDir 'CleanOptimize.exe'))) { throw '安装后缺少程序文件' }
    $published = Join-Path $dist "CleanOptimize-$Version-win-x64-selfcontained"
    $installedFileCount = 0
    foreach ($file in Get-ChildItem -LiteralPath $published -File -Recurse) {
        $relative = $file.FullName.Substring($published.Length + 1)
        if ((Get-FileHash -LiteralPath (Join-Path $installDir $relative) -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw "安装内容不一致: $relative" }
        $installedFileCount++
    }
    Write-Host "安装后全部 $installedFileCount 个发布文件 SHA256 校验通过。"
    Run-CheckedProcess (Join-Path $installDir 'CleanOptimize.exe') @('--selftest', '--selftest-log', ('"' + (Join-Path $dist 'installed-selftest.log') + '"'))
    Write-Host '安装与已安装程序自测通过。'
} finally {
    $uninstaller = Join-Path $installDir 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        Run-CheckedProcess $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $dist 'uninstall-smoke.log') + '"'))
    }
}
if (Test-Path (Join-Path $installDir 'CleanOptimize.exe')) { throw '卸载后仍残留程序文件' }
if (Test-Path "HKCU:\$uninstallKey") { throw '卸载后仍残留安装注册表项' }
Write-Host '卸载验证通过；所有包验收完成。'
Write-Host "绿色版验收目录: $zipDir"
