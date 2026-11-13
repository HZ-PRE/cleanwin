# 清理优化大师 发布打包脚本
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1 [-Version 1.0.0]
# 产物: dist/ 下 框架依赖版目录、自包含版目录、zip 安装包(如装了 Inno Setup)、SHA256SUMS.txt
param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$DotnetPath = "dotnet",
    [string]$IsccPath = "",
    [switch]$RequireInstaller
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'security-check.ps1')
$project = Join-Path $root "src\CleanOptimize.App\CleanOptimize.App.csproj"
$outDir = Join-Path $root "dist"
if (-not $Version) {
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $Version = [string]$projectXml.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw "版本号无效: $Version" }
if (-not (Get-Command $DotnetPath -ErrorAction SilentlyContinue)) { throw "未找到 .NET SDK: $DotnetPath" }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# 每次发布使用干净目录，避免上次的旧依赖混入产物；仅允许删除 dist 下的精确构建目录。
foreach ($leaf in @("CleanOptimize-$Version-net8-x64", "CleanOptimize-$Version-win-x64-selfcontained")) {
    $target = [IO.Path]::GetFullPath((Join-Path $outDir $leaf))
    $boundary = [IO.Path]::GetFullPath($outDir).TrimEnd('\') + '\'
    if (-not $target.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw "构建目录越界" }
    if (Test-Path -LiteralPath $target) {
        if ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "构建目录不能是链接" }
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
$ShortHash = ""
try { $ShortHash = (git rev-parse --short HEAD).Trim() } catch { }
if (-not $ShortHash) { $ShortHash = "0" }

Write-Host "==> 还原并发布 ($Configuration)"
& $DotnetPath publish $project -c $Configuration -r win-x64 --self-contained false -p:PublishReadyToRun=true `
    -p:Version=$Version -p:SourceRevisionId=$ShortHash -o (Join-Path $outDir "CleanOptimize-$Version-net8-x64") `
    -v minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "发布失败(框架依赖版)" }

Write-Host "==> 发布自包含版(无需安装 .NET)"
& $DotnetPath publish $project -c $Configuration -r win-x64 --self-contained true -p:PublishReadyToRun=true `
    -p:PublishSingleFile=false -p:Version=$Version -p:SourceRevisionId=$ShortHash -o (Join-Path $outDir "CleanOptimize-$Version-win-x64-selfcontained") `
    -v minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "发布失败(自包含版)" }

Write-Host "==> 移除调试符号 (*.pdb 含构建机路径，不随产物分发)"
Get-ChildItem (Join-Path $outDir "CleanOptimize-$Version-net8-x64") -Recurse -Include *.pdb | Remove-Item -Force
Get-ChildItem (Join-Path $outDir "CleanOptimize-$Version-win-x64-selfcontained") -Recurse -Include *.pdb | Remove-Item -Force

Write-Host "==> 校验自包含产物完整性（安装包与绿色包的全部依赖必须内置）"
$scDir = Join-Path $outDir "CleanOptimize-$Version-win-x64-selfcontained"
$requiredRuntime = @(
    "CleanOptimize.exe", "hostfxr.dll", "hostpolicy.dll", "coreclr.dll",
    "System.Private.CoreLib.dll", "WindowsBase.dll", "PresentationFramework.dll"
)
$missing = @($requiredRuntime | Where-Object { -not (Test-Path (Join-Path $scDir $_)) })
if ($missing.Count -gt 0) { throw "自包含产物缺少运行时文件: $($missing -join ', ')" }
Write-Host ("运行时完整性 ✓  (" + (Get-ChildItem $scDir -File).Count + " 个文件)")

Write-Host "==> 运行安全自测 (--selftest, 直接运行用户拿到的自包含 exe)"
$exe = Join-Path $scDir "CleanOptimize.exe"
# WinExe 用 & 调用不等待退出（且会锁住同目录 DLL 影响后续打包），必须 Start-Process -Wait
$p = Start-Process -FilePath $exe -ArgumentList @("--selftest", "--selftest-log", ('"' + (Join-Path $outDir "selftest.log") + '"')) -PassThru -WindowStyle Hidden `
    -RedirectStandardError (Join-Path $outDir "selftest-errors.log")
if (-not $p.WaitForExit(180000)) {
    $p.Kill()
    throw "安全自测超时，禁止发布"
}
$p.WaitForExit()
if ($p.ExitCode -ne 0) { throw "安全自测未通过，禁止发布！(PRD 第 53 章)" }
Write-Host "安全自测全部通过 ✓"

# —— 可选：代码签名（拿到证书后启用）——
# 硬件令牌/云令牌:  $env:CM_SIGN="1"; $env:CM_SIGN_SUBJECT="CN=你的公司或名字"
# PFX(旧证书/测试): $env:CM_SIGN="1"; $env:CM_SIGN_PFX="my.pfx"; $env:CM_SIGN_PWD="xxx"
if ($env:CM_SIGN -eq "1") {
    Write-Host "==> 代码签名"
    foreach ($d in @("CleanOptimize-$Version-net8-x64", "CleanOptimize-$Version-win-x64-selfcontained")) {
        $p = @{ Folder = Join-Path $outDir $d }
        if ($env:CM_SIGN_SUBJECT) { $p.Subject = $env:CM_SIGN_SUBJECT }
        if ($env:CM_SIGN_PFX)     { $p.PfxPath = $env:CM_SIGN_PFX; $p.PfxPassword = $env:CM_SIGN_PWD }
        & (Join-Path $PSScriptRoot "sign.ps1") @p
        if ($LASTEXITCODE -ne 0) { throw "签名失败，禁止发布" }
    }
}

# —— 安装包（检测 Inno Setup，装了就出安装包）——
$isccCandidates = @(
    $IsccPath,
    "C:\Program Files\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
)
$cmdIscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($cmdIscc) { $isccCandidates += $cmdIscc.Source }
$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($iscc) {
    Write-Host "==> 编译安装包 (Inno Setup)"
    & $iscc "/DMyAppVersion=$Version" "/DSrcDir=$(Join-Path $outDir "CleanOptimize-$Version-win-x64-selfcontained")" `
        (Join-Path $PSScriptRoot "setup.iss") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "安装包编译失败" }
    $setupExe = Join-Path $outDir "CleanOptimizeSetup-$Version.exe"
    if (-not (Test-Path $setupExe)) { throw "未找到安装包产物" }
    # 启用了签名时，连安装包一起签
    if ($env:CM_SIGN -eq "1") {
        $p = @{ File = $setupExe }
        if ($env:CM_SIGN_SUBJECT) { $p.Subject = $env:CM_SIGN_SUBJECT }
        if ($env:CM_SIGN_PFX)     { $p.PfxPath = $env:CM_SIGN_PFX; $p.PfxPassword = $env:CM_SIGN_PWD }
        & (Join-Path $PSScriptRoot "sign.ps1") @p
    }
    Write-Host "安装包: $setupExe"
}
else {
    if ($RequireInstaller) { throw "未检测到 Inno Setup，无法完成安装包发布" }
    Write-Host "未检测到 Inno Setup，跳过安装包（可 winget install JRSoftware.InnoSetup 后重跑）"
}

Write-Host "==> 打包 zip"
$zip = Join-Path $outDir "CleanOptimize-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $outDir "CleanOptimize-$Version-win-x64-selfcontained\*") `
    -DestinationPath $zip -CompressionLevel Optimal

Write-Host "==> 校验绿色包(zip)内置运行时"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPkg = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = $zipPkg.Entries | ForEach-Object { $_.FullName }
    $needInZip = $requiredRuntime
    $missZip = @($needInZip | Where-Object { $names -notcontains $_ })
    if ($missZip.Count -gt 0) { throw "zip 包缺少运行时文件: $($missZip -join ', ')" }
    foreach ($file in Get-ChildItem -LiteralPath $scDir -Recurse -File) {
        $relative = $file.FullName.Substring($scDir.Length + 1).Replace('\', '/')
        $entry = $zipPkg.GetEntry($relative)
        if (-not $entry -or $entry.Length -ne $file.Length) { throw "zip 内容不完整: $relative" }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '')
            if ($actual -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw "zip 文件校验失败: $relative" }
        } finally { $stream.Dispose(); $sha.Dispose() }
    }
} finally { $zipPkg.Dispose() }
Write-Host "zip 运行时完整性 ✓"

# 校验和必须先生成，再交给可选 Release 上传步骤。
Write-Host "==> 生成 SHA256SUMS.txt"
$hashTargets = @($zip)
if ($iscc) { $hashTargets += (Join-Path $outDir "CleanOptimizeSetup-$Version.exe") }
$lines = foreach ($t in $hashTargets) {
    "{0}  {1}" -f (Get-FileHash -Algorithm SHA256 -LiteralPath $t).Hash, (Split-Path $t -Leaf)
}
$lines | Set-Content -Encoding UTF8 (Join-Path $outDir "SHA256SUMS.txt")

Write-Host ""
Write-Host "===== 发布完成，dist 产物 ====="
Get-ChildItem $outDir -File | Where-Object { $_.Name -match $Version -or $_.Name -eq "SHA256SUMS.txt" } |
    Select-Object Name, @{n = "Size"; e = { "{0:N1} MB" -f ($_.Length / 1MB) }} | Format-Table
if ($env:CM_SIGN -ne "1") {
    Write-Host "提示: 当前产物未签名。拿到证书后运行: `$env:CM_SIGN='1'; `$env:CM_SIGN_SUBJECT='CN=名称' 再执行本脚本。"
}
