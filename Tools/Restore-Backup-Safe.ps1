# Restore a CC Suite Pro ZIP backup to a NEW, empty path; never overwrite original Mods.
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$BackupZip,
  [Parameter(Mandatory=$true)][string]$Destination,
  [switch]$Restore
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = [IO.Path]::GetFullPath($BackupZip)
$target = [IO.Path]::GetFullPath($Destination)
if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) { throw "No existe el ZIP: $zipPath" }
if (Test-Path -LiteralPath $target) { throw 'La carpeta destino debe NO existir. No se sobrescribe ninguna carpeta existente.' }
$parent = Split-Path -Parent $target
if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw "El directorio padre no existe: $parent" }
$stage = Join-Path $parent ('.ccsuite-restore-' + [guid]::NewGuid().ToString('N'))
$prefix = [IO.Path]::GetFullPath($stage).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
  if ($zip.Entries.Count -eq 0) { throw 'Archivo ZIP vacio.' }
  $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in $zip.Entries) {
    $relative = $entry.FullName.Replace('/', [IO.Path]::DirectorySeparatorChar).Replace('\', [IO.Path]::DirectorySeparatorChar)
    if ($relative.StartsWith([IO.Path]::DirectorySeparatorChar.ToString()) -or
        $relative.Contains(':') -or
        $relative -match '(^|[\\/])\.\.([\\/]|$)') { throw "Entrada ZIP insegura: $($entry.FullName)" }
    $resolved = [IO.Path]::GetFullPath([IO.Path]::Combine($stage, $relative))
    if (-not ($resolved + [IO.Path]::DirectorySeparatorChar).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -and
        -not $resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) {
       throw "Entrada fuera de carpeta destino: $($entry.FullName)"
    }
    if (-not $names.Add($relative)) { throw "Entrada duplicada: $relative" }
  }
  Write-Host "ZIP valido para restaurar a nueva carpeta: $target" -ForegroundColor Green
  Write-Host "Entradas: $($zip.Entries.Count)"
} finally { $zip.Dispose() }
if (-not $Restore) { Write-Host 'Modo revision: no se hizo ningun cambio. Repite con -Restore cuando quieras restaurar.'; return }
$ok = Read-Host 'Escribe RESTAURAR para extraer a la carpeta nueva'
if ($ok -cne 'RESTAURAR') { Write-Host 'Cancelado.'; return }
try {
  [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $stage)
  Move-Item -LiteralPath $stage -Destination $target
  Write-Host "Copia restaurada en: $target" -ForegroundColor Green
  Write-Host 'Compara los archivos y solo despues decide si reemplazar tus Mods. Este script no reemplaza tu carpeta activa.'
} catch {
  if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
  throw
}
