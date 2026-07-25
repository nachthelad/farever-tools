[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$GameDirectory,

    [string]$PayloadDirectory = $PSScriptRoot,

    [string]$ExpectedMinimapHash =
        '38888C303AD65F17E2C3B398D6B723750E4B694095A02FDE786DEF10C08F83B6'
)

$ErrorActionPreference = 'Stop'
$manifestName = 'farever-translate.manifest.json'
$backupDirectoryName = 'FareverTranslate.Backup'
$hostFiles = @(
    'FareverTranslate.Host.exe',
    'FareverTranslate.Host.dll',
    'FareverTranslate.Host.deps.json',
    'FareverTranslate.Host.runtimeconfig.json',
    'FareverTranslate.Core.dll'
)

function Get-NormalizedHash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-LeafName([string]$Name) {
    if ([IO.Path]::GetFileName($Name) -ne $Name) {
        throw "Nombre de archivo no permitido: $Name"
    }
}

if (Get-Process -Name 'Farever' -ErrorAction SilentlyContinue) {
    throw 'Cerrá Farever antes de instalar Farever Translate.'
}

$resolvedGame = (Resolve-Path -LiteralPath $GameDirectory).Path
$resolvedPayload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $resolvedGame 'Farever.exe') -PathType Leaf)) {
    throw 'La carpeta seleccionada no contiene Farever.exe.'
}

$manifestPath = Join-Path $resolvedGame $manifestName
if (Test-Path -LiteralPath $manifestPath) {
    throw 'Farever Translate ya figura como instalado. Desinstalalo primero.'
}

$originalPath = Join-Path $resolvedGame 'dinput8.dll'
$chainedPath = Join-Path $resolvedGame 'farever-minimap.dll'
if (-not (Test-Path -LiteralPath $originalPath -PathType Leaf)) {
    throw 'No se encontró el dinput8.dll del minimapa instalado.'
}
if (Test-Path -LiteralPath $chainedPath) {
    throw 'Ya existe farever-minimap.dll; no se modificó nada.'
}

$originalHash = Get-NormalizedHash $originalPath
if ($originalHash -ne $ExpectedMinimapHash.ToUpperInvariant()) {
    throw "El hash del minimapa no coincide. Encontrado: $originalHash"
}

$payloadFiles = @('dinput8.dll') + $hostFiles
foreach ($file in $payloadFiles) {
    Assert-LeafName $file
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedPayload $file) -PathType Leaf)) {
        throw "Falta el archivo del paquete: $file"
    }
}

$backupDirectory = Join-Path $resolvedGame $backupDirectoryName
$backupPath = Join-Path $backupDirectory 'dinput8.original.dll'
$installedFiles = [Collections.Generic.List[object]]::new()
$copiedFiles = [Collections.Generic.List[string]]::new()
$movedOriginal = $false

if (-not $PSCmdlet.ShouldProcess($resolvedGame, 'Instalar Farever Translate')) {
    return
}

try {
    New-Item -ItemType Directory -Path $backupDirectory | Out-Null
    Copy-Item -LiteralPath $originalPath -Destination $backupPath
    if ((Get-NormalizedHash $backupPath) -ne $originalHash) {
        throw 'El respaldo del minimapa no coincide byte por byte.'
    }

    Move-Item -LiteralPath $originalPath -Destination $chainedPath
    $movedOriginal = $true

    foreach ($file in $payloadFiles) {
        $source = Join-Path $resolvedPayload $file
        $destination = Join-Path $resolvedGame $file
        Copy-Item -LiteralPath $source -Destination $destination
        $copiedFiles.Add($file)
        $installedFiles.Add([ordered]@{
            Name = $file
            Sha256 = Get-NormalizedHash $destination
        })
    }

    $manifest = [ordered]@{
        FormatVersion = 1
        InstalledAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        OriginalDinput8Sha256 = $originalHash
        ChainedFile = 'farever-minimap.dll'
        BackupFile = "$backupDirectoryName\dinput8.original.dll"
        Files = $installedFiles
    }
    $json = $manifest | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText(
        $manifestPath,
        $json,
        [Text.UTF8Encoding]::new($false))
    Write-Host 'Farever Translate fue instalado correctamente.'
}
catch {
    foreach ($file in $copiedFiles) {
        $destination = Join-Path $resolvedGame $file
        if (Test-Path -LiteralPath $destination) {
            Remove-Item -LiteralPath $destination -Force
        }
    }
    if ($movedOriginal -and
        (Test-Path -LiteralPath $chainedPath) -and
        -not (Test-Path -LiteralPath $originalPath)) {
        Move-Item -LiteralPath $chainedPath -Destination $originalPath
    }
    if ((Test-Path -LiteralPath $backupPath) -and
        (Test-Path -LiteralPath $originalPath) -and
        (Get-NormalizedHash $backupPath) -eq (Get-NormalizedHash $originalPath)) {
        Remove-Item -LiteralPath $backupPath -Force
        if ((Get-ChildItem -LiteralPath $backupDirectory -Force).Count -eq 0) {
            Remove-Item -LiteralPath $backupDirectory
        }
    }
    throw
}
