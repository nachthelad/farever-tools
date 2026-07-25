[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$GameDirectory
)

$ErrorActionPreference = 'Stop'

function Get-NormalizedHash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-LeafName([string]$Name) {
    if ([IO.Path]::GetFileName($Name) -ne $Name) {
        throw "Nombre de archivo no permitido en el manifiesto: $Name"
    }
}

if (Get-Process -Name 'Farever' -ErrorAction SilentlyContinue) {
    throw 'Cerrá Farever antes de desinstalar Farever Translate.'
}

$resolvedGame = (Resolve-Path -LiteralPath $GameDirectory).Path
$manifestPath = Join-Path $resolvedGame 'farever-translate.manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'No se encontró el manifiesto de Farever Translate.'
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.FormatVersion -ne 1) {
    throw 'El formato del manifiesto no es compatible.'
}

$expectedOriginalHash = ([string]$manifest.OriginalDinput8Sha256).ToUpperInvariant()
$chainedName = [string]$manifest.ChainedFile
$backupRelative = [string]$manifest.BackupFile
Assert-LeafName $chainedName
if ($backupRelative -ne 'FareverTranslate.Backup\dinput8.original.dll') {
    throw 'La ruta de respaldo del manifiesto es inesperada.'
}

$chainedPath = Join-Path $resolvedGame $chainedName
$backupPath = Join-Path $resolvedGame $backupRelative
$currentProxy = Join-Path $resolvedGame 'dinput8.dll'
foreach ($path in @($chainedPath, $backupPath, $currentProxy)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Falta un archivo requerido para restaurar: $path"
    }
}
if ((Get-NormalizedHash $chainedPath) -ne $expectedOriginalHash -or
    (Get-NormalizedHash $backupPath) -ne $expectedOriginalHash) {
    throw 'El minimapa o su respaldo fueron modificados; se abortó la restauración.'
}

foreach ($file in $manifest.Files) {
    $name = [string]$file.Name
    Assert-LeafName $name
    $path = Join-Path $resolvedGame $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Falta un archivo instalado: $name"
    }
    $actualHash = Get-NormalizedHash $path
    if ($actualHash -ne ([string]$file.Sha256).ToUpperInvariant()) {
        throw "El archivo $name fue modificado; se abortó la desinstalación."
    }
}

if (-not $PSCmdlet.ShouldProcess($resolvedGame, 'Desinstalar y restaurar el minimapa')) {
    return
}

foreach ($file in $manifest.Files) {
    if ([string]$file.Name -ne 'dinput8.dll') {
        $path = Join-Path $resolvedGame ([string]$file.Name)
        Remove-Item -LiteralPath $path -Force
    }
}
Remove-Item -LiteralPath $currentProxy -Force
Move-Item -LiteralPath $chainedPath -Destination $currentProxy
if ((Get-NormalizedHash $currentProxy) -ne $expectedOriginalHash) {
    throw 'La restauración terminó con un hash inesperado. Conservá el respaldo.'
}

Remove-Item -LiteralPath $backupPath -Force
$backupDirectory = Split-Path -Parent $backupPath
if ((Get-ChildItem -LiteralPath $backupDirectory -Force).Count -eq 0) {
    Remove-Item -LiteralPath $backupDirectory
}
Remove-Item -LiteralPath $manifestPath -Force
Write-Host 'Farever Translate fue desinstalado y el minimapa quedó restaurado.'
