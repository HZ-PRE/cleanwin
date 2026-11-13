$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$source = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
# 本项目为离线应用。新增联网实现必须先显式改变并审查此门禁。
$forbidden = 'System\.Net\.(Http|Sockets)|\b(HttpClient|WebClient|WebRequest|TcpClient|UdpClient)\b|https?://|Invoke-WebRequest|Invoke-RestMethod|curl\.exe|wget\.exe'
$hits = @($source | Select-String -Pattern $forbidden)
if ($hits.Count) { $hits | ForEach-Object { Write-Host $_ }; throw '发现禁止的运行时联网入口' }

$langs = Get-ChildItem (Join-Path $root 'src\CleanOptimize.App\Assets\Langs\*.xaml')
$reference = $null
foreach ($file in $langs) {
    [xml]$doc = Get-Content -LiteralPath $file.FullName -Raw
    $keys = @($doc.DocumentElement.ChildNodes | Where-Object { $_.NodeType -eq 'Element' } |
        ForEach-Object { $_.GetAttribute('Key', 'http://schemas.microsoft.com/winfx/2006/xaml') })
    if (@($keys | Group-Object | Where-Object Count -gt 1).Count) { throw "重复语言资源: $($file.Name)" }
    if ($null -eq $reference) { $reference = $keys }
    if (@(Compare-Object $reference $keys).Count) { throw "语言资源不完整: $($file.Name)" }
}
Write-Host "离线源码门禁通过；$($langs.Count) 种语言、每种 $($reference.Count) 项资源一致。"
