$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$source = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
# Runtime networking is restricted to the explicitly enabled GitHub update service.
$forbidden = 'System\.Net\.(Http|Sockets)|\b(HttpClient|WebClient|WebRequest|TcpClient|UdpClient)\b|https?://|Invoke-WebRequest|Invoke-RestMethod|curl\.exe|wget\.exe'
$updateFiles = @('UpdateService.cs', 'UpdateDownloads.cs') | ForEach-Object { Join-Path $root "src\CleanOptimize.App\Services\$_" }
$hits = @($source | Where-Object { $_.FullName -notin $updateFiles } | Select-String -Pattern $forbidden)
if ($hits.Count) { $hits | ForEach-Object { Write-Host $_ }; throw '发现禁止的运行时联网入口' }
$allowedUrls = @('https://api.github.com/repos/HZ-PRE/cleanwin/releases/latest', 'https://github.com/HZ-PRE/cleanwin/releases/download/')
foreach ($file in $updateFiles) {
    $code = Get-Content -LiteralPath $file -Raw
    foreach ($match in [regex]::Matches($code, 'https?://[^"\s]+')) {
        if ($match.Value -notin $allowedUrls) { throw "更新服务出现未允许的地址: $($match.Value)" }
    }
    if ($code -match '\b(PostAsync|PutAsync|DeleteAsync|PostAsJsonAsync)\b|HttpMethod\.(Post|Put|Delete|Patch)|AllowAutoRedirect\s*=\s*true|UseCookies\s*=\s*true') { throw '更新服务出现上传或不受控重定向。' }
}

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
Write-Host "联网入口门禁通过（仅 GitHub 更新）；$($langs.Count) 种语言、每种 $($reference.Count) 项资源一致。"

# Check literal localization references as well as equality between generated dictionaries.
# Constructed rule/maintenance keys are covered by WPF self-tests.
$knownKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($key in $reference) { [void]$knownKeys.Add($key) }
$uiFiles = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.xaml' -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|Langs)\\' }
foreach ($file in @($source) + @($uiFiles)) {
    $code = Get-Content -LiteralPath $file.FullName -Raw
    $pattern = '(?:\bLoc|\bCoreLocalizer)\.T\("(?<key>[^"\r\n]+)"|\{(?:DynamicResource|StaticResource)\s+(?<key>[a-z][a-z0-9_]*(?:\.[a-z0-9_]+)+)\}'
    foreach ($match in [regex]::Matches($code, $pattern)) {
        $key = $match.Groups['key'].Value
        if (-not $knownKeys.Contains($key)) { throw "缺少语言资源: $key ($($file.Name))" }
    }
}
Write-Host '静态语言资源引用完整。'
