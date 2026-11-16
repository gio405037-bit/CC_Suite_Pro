[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$base = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$paths = @(& git -C $base ls-files)
if ($LASTEXITCODE -ne 0 -or $paths.Count -lt 20) { throw 'Repositorio incompleto o Git no disponible.' }
$forbidden = '(?i)(^|/)(AI|Mods|Data|Pruebas|PatchBackups|PatchDiagnostics|Cache|Logs|TestResults|bin|obj|publish|dist|QA|Synthetic|Fixtures)(/|$)|(?i)\.(package|ts4script|py|pfx|p12|pem|key|sqlite|db|zip|7z|rar|exe|dll|pdb|log)$'
$secrets = '(?i)(-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|github_pat_[a-z0-9_]{20,}|gh[pousr]_[a-z0-9_]{20,}|AKIA[0-9A-Z]{16}|sk-[A-Za-z0-9_-]{24,})'
$extensionText = @('.cs','.csproj','.xaml','.json','.ps1','.props','.targets','.yml','.yaml','.md','.txt','.config','.resx','.svg','.gitignore')
$bad = 0
foreach ($path in $paths) {
    $rel = ([string]$path).Replace('\','/')
    if ($rel -match $forbidden -or $rel -match '(^|/)\.env(?:\..*)?$') { $bad++; continue }
    $absolute = Join-Path $base ($rel.Replace('/','\'))
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) { throw 'Archivo versionado ausente.' }
    $item = Get-Item -LiteralPath $absolute
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Enlace simbolico versionado no permitido.' }
    if ($item.Length -gt 30MB) { throw 'Archivo versionado de tamano excesivo.' }
    if ([IO.Path]::GetExtension($rel).ToLowerInvariant() -in $extensionText -and $item.Length -lt 5MB) {
        $content = [IO.File]::ReadAllText($absolute)
        if ([regex]::IsMatch($content,$secrets)) { throw 'Posible credencial en codigo versionado.' }
    }
}
if ($bad -gt 0) { throw ('Archivos privados o prohibidos versionados: ' + $bad) }
foreach ($required in @('CCSuitePro.csproj','Tests\ReleaseSmoke\ReleaseSmoke.csproj','Tests\WinterSmoke\WinterSmoke.csproj','Tests\Functional\Functional.csproj')) {
    if (-not (Test-Path -LiteralPath (Join-Path $base $required) -PathType Leaf)) { throw 'Componente requerido ausente.' }
}
Write-Host 'Revision automatica basica de contenido: PASS. No sustituye revision humana.' -ForegroundColor Green
