[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$GameDirectory,
    [switch]$NoFolderPicker
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'InstallSupport.ps1')

$resolvedGame = Find-FareverGameDirectory `
    -GameDirectory $GameDirectory `
    -StartingDirectory (Split-Path -Parent $PSScriptRoot) `
    -RequireManifest `
    -NoFolderPicker:$NoFolderPicker
if (Test-FareverRunningInDirectory -GameDirectory $resolvedGame) {
    throw 'Cerrá Farever antes de desinstalar Farever Translate.'
}
$installation =
    Read-ValidatedInstallation -GameDirectory $resolvedGame
$currentProxy = Join-Path $resolvedGame 'dinput8.dll'

if (-not $PSCmdlet.ShouldProcess(
        $resolvedGame,
        'Desinstalar Farever Translate y restaurar el minimapa')) {
    return
}

foreach ($name in $installation.InstalledNames) {
    if ($name -ne 'dinput8.dll') {
        Remove-Item -LiteralPath (Join-Path $resolvedGame $name) -Force
    }
}
Remove-Item -LiteralPath $currentProxy -Force
Move-Item -LiteralPath $installation.ChainedPath `
    -Destination $currentProxy
if ((Get-NormalizedHash $currentProxy) -ne
    $installation.OriginalHash) {
    throw 'La restauración terminó con un hash inesperado. Conservá el respaldo.'
}

Remove-Item -LiteralPath $installation.BackupPath -Force
$backupDirectory = Split-Path -Parent $installation.BackupPath
if (@(Get-ChildItem -LiteralPath $backupDirectory -Force).Count -eq 0) {
    Remove-Item -LiteralPath $backupDirectory
}
Remove-Item -LiteralPath $installation.ManifestPath -Force
Write-Host 'Farever Translate fue desinstalado y el minimapa quedó restaurado.'
