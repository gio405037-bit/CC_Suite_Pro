# CC Suite Pro v1.2 RC2 - Local diagnostics
[CmdletBinding()]
param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$IncludeCrashLog,
    [switch]$IncludePaths
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ProjectPath)
$dest = Join-Path $root '_diagnostics'
if (-not (Test-Path -LiteralPath $dest)) { New-Item -ItemType Directory -Path $dest | Out-Null }
$stamp = Get-Date -Format yyyyMMdd_HHmmss
$file = Join-Path $dest "Report_$stamp.txt"
$redact = { param([string]$inputString)
    if ($IncludePaths) { return $inputString }
    $home = [regex]::Escape($env:USERPROFILE)
    $result = $inputString -replace $home, '<USER_HOME>'
    $result = $result -replace '[A-Za-z]:\\[^\r\n\"]+', '<LOCAL_PATH>'
    return $result
}
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('CC Suite Pro diagnostics | local-only | no network connection')
$lines.Add("Timestamp UTC: $([DateTime]::UtcNow.ToString('o'))")
$lines.Add("OS: $([Environment]::OSVersion.VersionString)")
$lines.Add("64-bit OS: $([Environment]::Is64BitOperatingSystem)")
$lines.Add("Windows: $([Environment]::OSVersion.Platform -eq 'Win32NT')")
$lines.Add("Project present: $(Test-Path (Join-Path $root 'CCSuitePro.csproj'))")
$lines.Add("Executable present: $(Test-Path (Join-Path $root 'bin\Debug\net8.0-windows\win-x64\CCSuitePro.exe'))")
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    $info = & dotnet --info 2>&1 | Out-String
    $lines.Add((& $redact $info))
} else { $lines.Add('dotnet SDK: not found') }
$crash = Join-Path $env:LOCALAPPDATA 'CCSuitePro\Logs\crashes.log'
$lines.Add("Local crash log present: $(Test-Path $crash)")
if ($IncludeCrashLog -and (Test-Path $crash)) {
    $lines.Add('=== Crash log (requested explicitly) ===')
    $lines.Add((& $redact ((Get-Content -LiteralPath $crash -Tail 120) -join [Environment]::NewLine)))
}
[IO.File]::WriteAllLines($file,$lines,[Text.UTF8Encoding]::new($false))
Write-Host "Informe generado: $file" -ForegroundColor Green
Write-Host 'Revisalo antes de compartirlo; no incluye registros de error sin -IncludeCrashLog.'
