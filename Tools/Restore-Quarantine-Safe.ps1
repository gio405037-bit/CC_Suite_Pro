[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath
)
$ErrorActionPreference = 'Stop'

$manifestFull = [IO.Path]::GetFullPath($ManifestPath)
if (-not (Test-Path -LiteralPath $manifestFull -PathType Leaf)) {
    throw "Manifest not found: $manifestFull"
}
$record = Get-Content -LiteralPath $manifestFull -Raw -Encoding UTF8 | ConvertFrom-Json
$source = [IO.Path]::GetFullPath([string]$record.quarantinedPath)
$target = [IO.Path]::GetFullPath([string]$record.originalPath)
$expected = ([string]$record.sha256).ToUpperInvariant()
$quarantineRoot = [IO.Path]::GetFullPath((Split-Path -Parent $manifestFull))
$quarantinePrefix = $quarantineRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $source.StartsWith($quarantinePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Manifest source must point to a file inside the selected quarantine folder.'
}
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Quarantined file not found: $source" }
if ([string]::IsNullOrWhiteSpace($expected) -or $expected -notmatch '^[A-F0-9]{64}$') { throw 'Manifest SHA-256 is missing or invalid.' }
if (Test-Path -LiteralPath $target) { throw "Restore refused: the original path already exists. No file was overwritten: $target" }

$actual = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actual -ne $expected) { throw 'Restore refused: quarantined file SHA-256 does not match its manifest.' }
$parent = Split-Path -Parent $target
New-Item -ItemType Directory -Path $parent -Force | Out-Null
Copy-Item -LiteralPath $source -Destination $target -ErrorAction Stop
$restored = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToUpperInvariant()
if ($restored -ne $expected) {
    Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
    throw 'Restore verification failed. The copied file was removed; the quarantine original remains untouched.'
}
Write-Host 'RESTORE VERIFIED' -ForegroundColor Green
Write-Host "Restored to: $target"
Write-Host "Quarantine copy retained at: $source"
