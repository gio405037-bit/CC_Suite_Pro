[CmdletBinding()]
param([string]$ProjectPath = (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ProjectPath)
$manifest = Join-Path $root 'SOURCE_MANIFEST_SHA256.txt'
if (-not (Test-Path -LiteralPath $manifest)) { throw "Manifest not found: $manifest" }
$checked = 0
foreach ($line in Get-Content -LiteralPath $manifest -Encoding UTF8) {
  if ([string]::IsNullOrWhiteSpace($line)) { continue }
  if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw "Invalid manifest line: $line" }
  $expected = $Matches[1].ToUpperInvariant(); $relative = $Matches[2]
  $path = Join-Path $root $relative
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing: $relative" }
  $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
  if ($actual -ne $expected) { throw "Hash mismatch: $relative" }
  $checked++
}
Write-Host "Manifest OK: $checked source files verified." -ForegroundColor Green
