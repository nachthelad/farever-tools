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
$installMode = [string]$installation.InstallMode
$action = if ($installMode -eq 'ChainedMinimap') {
    'Desinstalar Farever Translate y restaurar el minimapa'
}
else {
    'Desinstalar Farever Translate'
}

if (-not $PSCmdlet.ShouldProcess(
        $resolvedGame,
        $action)) {
    return
}

foreach ($name in $installation.InstalledNames) {
    if ($name -ne 'dinput8.dll') {
        Remove-Item -LiteralPath (Join-Path $resolvedGame $name) -Force
    }
}
Remove-Item -LiteralPath $currentProxy -Force

if ($installMode -eq 'ChainedMinimap') {
    Move-Item -LiteralPath $installation.ChainedPath `
        -Destination $currentProxy
    if ((Get-NormalizedHash $currentProxy) -ne
        $installation.OriginalHash) {
        throw (
            'La restauración terminó con un hash inesperado. ' +
            'Conservá el respaldo.')
    }

    Remove-Item -LiteralPath $installation.BackupPath -Force
    $backupDirectory = Split-Path -Parent $installation.BackupPath
    if (@(Get-ChildItem -LiteralPath $backupDirectory -Force).
            Count -eq 0) {
        Remove-Item -LiteralPath $backupDirectory
    }
}
Remove-Item -LiteralPath $installation.ManifestPath -Force
if ($installMode -eq 'ChainedMinimap') {
    Write-Host (
        'Farever Translate fue desinstalado y el minimapa quedó ' +
        'restaurado.')
}
else {
    Write-Host 'Farever Translate fue desinstalado.'
}
