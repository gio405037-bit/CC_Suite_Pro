[CmdletBinding()]
param([string]$ProjectPath = (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ProjectPath)
foreach ($name in @('MainWindow.xaml', 'SplashWindow.xaml', 'App.xaml', 'OrganizeDialog.xaml', 'CCSuitePro.csproj')) {
    $path = Join-Path $root $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Falta $name" }
    try { $null = [xml][IO.File]::ReadAllText($path) }
    catch { throw "XML/XAML invalido: $name : $($_.Exception.Message)" }
}
$main = [IO.File]::ReadAllText((Join-Path $root 'MainWindow.xaml'))
$model = [IO.File]::ReadAllText((Join-Path $root 'Models\ModFile.cs'))
$csproj = [IO.File]::ReadAllText((Join-Path $root 'CCSuitePro.csproj'))
if ($model -notmatch 'public\s+string\s+FileSizeFormatted\s*\{\s*get\s*\{') {
    throw 'No se encontro la propiedad FileSizeFormatted de solo lectura esperada. Revisar manualmente.'
}
$bindings = [regex]::Matches($main, '\{Binding\s+FileSizeFormatted\b([^}]*)\}')
if ($bindings.Count -ne 3) { throw "Se esperaban 3 bindings FileSizeFormatted y hay $($bindings.Count). Abortar para revisar." }
foreach ($binding in $bindings) {
    $modes = [regex]::Matches($binding.Value, '(?i)\bMode\s*=\s*([A-Za-z]+)')
    if ($modes.Count -ne 1 -or $modes[0].Groups[1].Value -ne 'OneWay') {
        throw "Binding incorrecto o duplicado: $($binding.Value)"
    }
}
if ($csproj -notmatch 'DefaultItemExcludes' -or $csproj -notmatch '<Page Remove="Backup_') {
    throw 'Falta la exclusion permanente de XAML de respaldos en el proyecto.'
}
if ($csproj -notmatch '<Version>2\.0\.0-rc\.3\.1</Version>') {
    throw 'La version del proyecto no corresponde a RC3.1.'
}
Write-Host 'RC3.1: XML valido, 3 bindings OneWay verificados y respaldos excluidos.' -ForegroundColor Green
