param([switch]$Apply)
$ErrorActionPreference = 'Stop'
# Maintenance for old test versions only. No whole-bin emptying and no application user data.
$profileRoot = [Environment]::GetFolderPath('UserProfile').TrimEnd('\')
$tempRoot = [IO.Path]::GetTempPath().TrimEnd('\')
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$uuid = '[0-9a-f]{32}'
$patterns = @(
    ('^' + [regex]::Escape($tempRoot) + '\\CleanOptimizeEmpty_' + $uuid + '\\emptyA$'),
    ('^' + [regex]::Escape($tempRoot) + '\\CleanOptimizeTestData_' + $uuid + '\\empty-ui\\leaf$'),
    ('^' + [regex]::Escape($profileRoot) + '\\CleanOptimizeToolTest_' + $uuid + '\\empty-tree$'),
    ('^' + [regex]::Escape($profileRoot) + '\\CleanOptimizeDuplicateTest_' + $uuid + '\\recycle\.bin$')
)
function Assert-NoLink([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Link in fixture path' }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}
function Assert-EmptyTree([string]$Path) {
    Assert-NoLink $Path
    foreach ($entry in Get-ChildItem -LiteralPath $Path -Force) {
        if (-not $entry.PSIsContainer) { throw 'Fixture is no longer an empty tree' }
        Assert-EmptyTree $entry.FullName
    }
}
$count = 0
foreach ($driveRoot in @([IO.Path]::GetPathRoot($profileRoot), [IO.Path]::GetPathRoot($tempRoot)) | Select-Object -Unique) {
    $bin = [IO.Path]::GetFullPath((Join-Path $driveRoot ('$Recycle.Bin\' + $sid)))
    if (-not (Test-Path -LiteralPath $bin)) { continue }
    Assert-NoLink $bin
    foreach ($metadata in Get-ChildItem -LiteralPath $bin -Filter '$I*' -File -Force) {
        if ($metadata.Length -lt 28 -or $metadata.Length -gt 65536) { continue }
        Assert-NoLink $metadata.FullName
        $bytes = [IO.File]::ReadAllBytes($metadata.FullName)
        $version = [BitConverter]::ToInt64($bytes, 0)
        if ($version -notin @(1, 2)) { continue }
        $offset = if ($version -eq 2) { 28 } else { 24 }
        $original = [Text.Encoding]::Unicode.GetString($bytes, $offset, $bytes.Length - $offset).TrimEnd([char]0)
        if (-not @($patterns | Where-Object { $original -match $_ }).Count) { continue }
        $payload = [IO.Path]::GetFullPath((Join-Path $bin ('$R' + $metadata.Name.Substring(2))))
        if ([IO.Path]::GetDirectoryName($payload) -ne $bin -or [IO.Path]::GetDirectoryName($metadata.FullName) -ne $bin) { throw 'Recycle path escaped its parent' }
        Assert-NoLink $payload
        if ($original.EndsWith('\recycle.bin', [StringComparison]::OrdinalIgnoreCase)) {
            if ((Get-Item -LiteralPath $payload -Force).Length -ne 4096) { throw 'Unexpected test file size' }
            if (@([IO.File]::ReadAllBytes($payload) | Where-Object { $_ -ne 0 }).Count) { throw 'Unexpected test file content' }
        } else { Assert-EmptyTree $payload }
        $count++
        if ($Apply) {
            Remove-Item -LiteralPath $payload -Recurse -Force
            Remove-Item -LiteralPath $metadata.FullName -Force
        }
    }
}
Write-Output "Verified old test fixtures: $count; removed: $([bool]$Apply)"
