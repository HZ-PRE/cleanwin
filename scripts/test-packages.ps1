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
$fixtureProcesses = [Collections.Generic.List[Diagnostics.Process]]::new()
$fixtureDataDirs = [Collections.Generic.List[string]]::new()
$fixtureReadyFiles = [Collections.Generic.List[string]]::new()
$portableFixture = $null
$hiddenFixture = $null
function Start-UninstallFixture([string]$Exe, [string]$Label, [string[]]$Extra = @()) {
    $readyFile = Join-Path $dist ('uninstall-ready-' + [Guid]::NewGuid().ToString('N') + '.txt')
    $fixtureReadyFiles.Add($readyFile)
    $arguments = @('--uninstall-lifetime-selftest', '--ready-file', ('"' + $readyFile + '"'),
        '--selftest-log', ('"' + (Join-Path $dist ("uninstall-running-$Label.log")) + '"')) + $Extra
    $fixture = Start-Process -FilePath $Exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $fixtureProcesses.Add($fixture)
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (-not (Test-Path -LiteralPath $readyFile)) {
        if ($fixture.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw "运行中卸载样本启动失败: $Label" }
        Start-Sleep -Milliseconds 100
    }
    $dataDir = [IO.Path]::GetFullPath((Get-Content -LiteralPath $readyFile -Raw).Trim())
    $tempBoundary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $dataDir.StartsWith($tempBoundary, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($dataDir) -notmatch '^CleanOptimizeTestData_[0-9a-f]{32}$') { throw '卸载样本数据目录越界' }
    $fixtureDataDirs.Add($dataDir)
    return $fixture
}
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
    $installedExe = Join-Path $installDir 'CleanOptimize.exe'
    $portableExe = Join-Path $zipDir 'CleanOptimize.exe'
    $portableFixture = Start-UninstallFixture $portableExe 'portable' @('--hidden')
    $normalFixture = Start-UninstallFixture $installedExe 'normal'
    Run-CheckedProcess $installedExe @('--shutdown-for-uninstall')
    if (-not $normalFixture.WaitForExit(10000) -or $normalFixture.ExitCode -ne 0 -or $portableFixture.HasExited) {
        throw '普通窗口退出失败或误结束了绿色版'
    }
    if (-not (Select-String -LiteralPath (Join-Path $dist 'uninstall-running-normal.log') -Pattern '\[PASS\]' -Quiet)) {
        throw '普通窗口未走正常关闭流程'
    }
    Write-Host '运行中退出：普通窗口正常关闭；其他目录绿色版保持运行。'
    $legacyFixture = Start-UninstallFixture $installedExe 'legacy' @('--ignore-uninstall-signal', '--hidden')
    Run-CheckedProcess $installedExe @('--shutdown-for-uninstall')
    if (-not $legacyFixture.WaitForExit(10000) -or $portableFixture.HasExited) { throw '旧版本定向结束失败' }
    Write-Host '运行中退出：不响应退出信号的旧版本定向结束；绿色版保持运行。'
    $hiddenFixture = Start-UninstallFixture $installedExe 'hidden' @('--hidden')
} finally {
    try {
        $uninstaller = Join-Path $installDir 'unins000.exe'
        if (Test-Path -LiteralPath $uninstaller) {
            Run-CheckedProcess $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $dist 'uninstall-smoke.log') + '"'))
        }
        if ($null -ne $hiddenFixture) {
            if (-not $hiddenFixture.WaitForExit(10000) -or $hiddenFixture.ExitCode -ne 0 -or $portableFixture.HasExited) {
                throw '实际卸载未结束托盘进程，或误结束绿色版'
            }
            if (-not (Select-String -LiteralPath (Join-Path $dist 'uninstall-running-hidden.log') -Pattern '\[PASS\]' -Quiet)) {
                throw '托盘进程未正常退出'
            }
            Write-Host '实际卸载：自动正常退出托盘进程；其他目录绿色版保持运行。'
        }
    } finally {
        if ($null -ne $portableFixture -and -not $portableFixture.HasExited) {
            Run-CheckedProcess (Join-Path $zipDir 'CleanOptimize.exe') @('--shutdown-for-uninstall')
        }
        foreach ($fixture in $fixtureProcesses) {
            if (-not $fixture.HasExited) { $fixture.Kill(); $fixture.WaitForExit() }
            $fixture.Dispose()
        }
        foreach ($dataDir in $fixtureDataDirs) {
            if (Test-Path -LiteralPath $dataDir) {
                if ((Get-Item -LiteralPath $dataDir -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '样本目录不能是链接' }
                if (@(Get-ChildItem -LiteralPath $dataDir -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw '样本目录中发现链接' }
                Remove-Item -LiteralPath $dataDir -Recurse -Force
            }
        }
        foreach ($readyFile in $fixtureReadyFiles) { if (Test-Path -LiteralPath $readyFile) { Remove-Item -LiteralPath $readyFile -Force } }
    }
}
if (Test-Path (Join-Path $installDir 'CleanOptimize.exe')) { throw '卸载后仍残留程序文件' }
if (Test-Path "HKCU:\$uninstallKey") { throw '卸载后仍残留安装注册表项' }
Write-Host '卸载验证通过；所有包验收完成。'
Write-Host "绿色版验收目录: $zipDir"
