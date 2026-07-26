[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$GameDirectory,
    [string]$PayloadDirectory = (Split-Path -Parent $PSScriptRoot),
    [hashtable]$SupportedMinimapHashes,
    [string[]]$SupportedGameBuildHashes,
    [switch]$NoFolderPicker,
    [switch]$SkipRuntimeCheck,
    [int]$TestFailAfterReplacements
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'InstallSupport.ps1')

if ($null -eq $SupportedMinimapHashes) {
    $SupportedMinimapHashes = @{
        '38888C303AD65F17E2C3B398D6B723750E4B694095A02FDE786DEF10C08F83B6' =
            '1.2.3'
        '2C22ECE52A9F7583AC2AA694BD91BC0B4488A69F216C0C249B4F15422D4BFC66' =
            '1.2.4'
    }
}
if ($null -eq $SupportedGameBuildHashes) {
    $SupportedGameBuildHashes = @(
        '7C3CA4DD765B50972B0EB524F82F9943A1D24D9EA52CBBA97C7F87D5D840E7A0'
    )
}
$supportedBuildSet = @($SupportedGameBuildHashes | ForEach-Object {
    $_.ToUpperInvariant()
})

if (-not $SkipRuntimeCheck) {
    Assert-WindowsDesktopRuntime
}

$resolvedPayload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$payloadManifest =
    Read-PayloadManifest -PayloadDirectory $resolvedPayload
$resolvedGame = Find-FareverGameDirectory `
    -GameDirectory $GameDirectory `
    -StartingDirectory $resolvedPayload `
    -NoFolderPicker:$NoFolderPicker
if (Test-FareverRunningInDirectory -GameDirectory $resolvedGame) {
    throw 'Cerrá Farever antes de instalar Farever Translate.'
}

$gameBuildPath = Join-Path $resolvedGame 'hlboot.dat'
if (-not (Test-Path -LiteralPath $gameBuildPath -PathType Leaf)) {
    throw 'La carpeta de Farever no contiene hlboot.dat.'
}
$gameBuildHash = Get-NormalizedHash $gameBuildPath
if ($gameBuildHash -notin $supportedBuildSet) {
    throw "Versión de Farever no compatible. hlboot.dat: $gameBuildHash"
}

$manifestPath =
    Join-Path $resolvedGame 'farever-translate.manifest.json'
$payloadNames = @($payloadManifest.Files | ForEach-Object {
    [string]$_.Name
})
$stagePath = Join-Path $resolvedGame (
    '.farever-translate-stage-' + [Guid]::NewGuid().ToString('N'))
$rollbackPath = Join-Path $resolvedGame (
    '.farever-translate-rollback-' + [Guid]::NewGuid().ToString('N'))

function New-InstalledManifest {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Standalone', 'ChainedMinimap')]
        [string]$InstallMode,
        [AllowNull()]$MinimapHash,
        [AllowNull()]$MinimapVersion,
        [string]$InstalledAtUtc
    )

    $originalHashValue = $null
    $minimapVersionValue = $null
    $chainedFile = $null
    $backupFile = $null
    if ($InstallMode -eq 'ChainedMinimap') {
        if ([string]::IsNullOrWhiteSpace([string]$MinimapHash) -or
            [string]::IsNullOrWhiteSpace([string]$MinimapVersion)) {
            throw 'Faltan datos del minimapa encadenado.'
        }
        $originalHashValue = [string]$MinimapHash
        $minimapVersionValue = [string]$MinimapVersion
        $chainedFile = 'farever-minimap.dll'
        $backupFile =
            'FareverTranslate.Backup\dinput8.original.dll'
    }

    $files = foreach ($name in $payloadNames) {
        [ordered]@{
            Name = $name
            Sha256 = Get-NormalizedHash (Join-Path $resolvedGame $name)
        }
    }
    return [ordered]@{
        FormatVersion = 3
        InstallMode = $InstallMode
        ProductVersion = [string]$payloadManifest.ProductVersion
        InstalledAtUtc = $InstalledAtUtc
        UpdatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        GameBuildSha256 = $gameBuildHash
        MinimapVersion = $minimapVersionValue
        OriginalDinput8Sha256 = $originalHashValue
        ChainedFile = $chainedFile
        BackupFile = $backupFile
        Files = @($files)
    }
}

function Stage-Payload {
    New-Item -ItemType Directory -Path $stagePath | Out-Null
    foreach ($file in $payloadManifest.Files) {
        $name = [string]$file.Name
        Copy-Item -LiteralPath (Join-Path $resolvedPayload $name) `
            -Destination (Join-Path $stagePath $name)
        if ((Get-NormalizedHash (Join-Path $stagePath $name)) -ne
            ([string]$file.Sha256).ToUpperInvariant()) {
            throw "La copia temporal no coincide: $name"
        }
    }
}

function Copy-StagedPayload {
    param(
        [switch]$NoReplace,
        [AllowNull()]
        [Collections.Generic.List[string]]$CopiedNames
    )

    $count = 0
    foreach ($name in $payloadNames) {
        $source = Join-Path $stagePath $name
        $destination = Join-Path $resolvedGame $name
        if ($NoReplace) {
            [IO.File]::Copy($source, $destination, $false)
        }
        else {
            Copy-Item -LiteralPath $source `
                -Destination $destination -Force
        }
        if ($null -ne $CopiedNames) {
            $CopiedNames.Add($name)
        }
        $count++
        if ($TestFailAfterReplacements -gt 0 -and
            $count -ge $TestFailAfterReplacements) {
            throw 'Fallo de actualización solicitado por la prueba.'
        }
    }
}

if (-not $PSCmdlet.ShouldProcess(
        $resolvedGame,
        "Instalar Farever Translate $($payloadManifest.ProductVersion)")) {
    return
}

$preserveRollback = $false
try {
    Stage-Payload
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        $installation =
            Read-ValidatedInstallation -GameDirectory $resolvedGame
        $installMode = [string]$installation.InstallMode
        $minimapVersion = $null
        if ($installMode -eq 'ChainedMinimap') {
            $minimapVersion =
                [string]$SupportedMinimapHashes[
                    $installation.OriginalHash]
            if ([string]::IsNullOrWhiteSpace($minimapVersion)) {
                throw (
                    'La versión instalada del minimapa ya no está ' +
                    "soportada. Hash: $($installation.OriginalHash)")
            }
        }

        New-Item -ItemType Directory -Path $rollbackPath | Out-Null
        Copy-Item -LiteralPath $installation.ManifestPath `
            -Destination (Join-Path $rollbackPath 'manifest.json')
        foreach ($name in $installation.InstalledNames) {
            Copy-Item -LiteralPath (Join-Path $resolvedGame $name) `
                -Destination (Join-Path $rollbackPath $name)
        }

        try {
            Copy-StagedPayload
            foreach ($oldName in $installation.InstalledNames) {
                if ($oldName -notin $payloadNames) {
                    Remove-Item -LiteralPath (
                        Join-Path $resolvedGame $oldName) -Force
                }
            }
            $installedAt = [string]$installation.Manifest.InstalledAtUtc
            if ([string]::IsNullOrWhiteSpace($installedAt)) {
                $installedAt = [DateTimeOffset]::UtcNow.ToString('O')
            }
            $newManifest = New-InstalledManifest `
                -InstallMode $installMode `
                -MinimapHash $installation.OriginalHash `
                -MinimapVersion $minimapVersion `
                -InstalledAtUtc $installedAt
            Write-JsonAtomic -Path $manifestPath -Value $newManifest
            $null = Read-ValidatedInstallation -GameDirectory $resolvedGame
        }
        catch {
            $updateError = $_
            try {
                foreach ($name in $payloadNames) {
                    if ($name -notin $installation.InstalledNames) {
                        $path = Join-Path $resolvedGame $name
                        if (Test-Path -LiteralPath $path) {
                            Remove-Item -LiteralPath $path -Force
                        }
                    }
                }
                foreach ($name in $installation.InstalledNames) {
                    Copy-Item -LiteralPath (
                        Join-Path $rollbackPath $name) `
                        -Destination (Join-Path $resolvedGame $name) `
                        -Force
                }
                Copy-Item -LiteralPath (
                    Join-Path $rollbackPath 'manifest.json') `
                    -Destination $manifestPath -Force
            }
            catch {
                $preserveRollback = $true
                throw (
                    'La actualización falló y no pudo restaurarse. ' +
                    "Conservá la recuperación en: $rollbackPath")
            }
            throw $updateError
        }
        Write-Host (
            "Farever Translate se actualizó a " +
            "$($payloadManifest.ProductVersion).")
    }
    else {
        $originalPath = Join-Path $resolvedGame 'dinput8.dll'
        $chainedPath = Join-Path $resolvedGame 'farever-minimap.dll'
        $backupDirectory =
            Join-Path $resolvedGame 'FareverTranslate.Backup'
        $backupPath =
            Join-Path $backupDirectory 'dinput8.original.dll'
        if ((Test-Path -LiteralPath $chainedPath) -or
            (Test-Path -LiteralPath $backupDirectory)) {
            throw 'Hay restos de otra instalación; no se modificó nada.'
        }
        foreach ($name in $payloadNames) {
            if ($name -ne 'dinput8.dll' -and
                (Test-Path -LiteralPath (
                    Join-Path $resolvedGame $name))) {
                throw (
                    'Hay archivos de otra instalación sin manifiesto; ' +
                    'no se modificó nada.')
            }
        }

        $installMode = 'Standalone'
        $originalHash = $null
        $minimapVersion = $null
        if (Test-Path -LiteralPath $originalPath -PathType Leaf) {
            $originalHash = Get-NormalizedHash $originalPath
            $minimapVersion =
                [string]$SupportedMinimapHashes[$originalHash]
            if ([string]::IsNullOrWhiteSpace($minimapVersion)) {
                throw (
                    'Ya existe un dinput8.dll desconocido; no se ' +
                    "modificó. Hash encontrado: $originalHash")
            }
            $installMode = 'ChainedMinimap'
        }

        $movedOriginal = $false
        $copiedNames =
            [Collections.Generic.List[string]]::new()
        try {
            if ($installMode -eq 'ChainedMinimap') {
                New-Item -ItemType Directory -Path $backupDirectory |
                    Out-Null
                Copy-Item -LiteralPath $originalPath `
                    -Destination $backupPath
                if ((Get-NormalizedHash $backupPath) -ne
                    $originalHash) {
                    throw 'El respaldo no coincide byte por byte.'
                }
                Move-Item -LiteralPath $originalPath `
                    -Destination $chainedPath
                $movedOriginal = $true
            }
            Copy-StagedPayload `
                -NoReplace -CopiedNames $copiedNames
            $newManifest = New-InstalledManifest `
                -InstallMode $installMode `
                -MinimapHash $originalHash `
                -MinimapVersion $minimapVersion `
                -InstalledAtUtc ([DateTimeOffset]::UtcNow.ToString('O'))
            Write-JsonAtomic -Path $manifestPath -Value $newManifest
            $null = Read-ValidatedInstallation -GameDirectory $resolvedGame
        }
        catch {
            foreach ($name in $copiedNames) {
                $path = Join-Path $resolvedGame $name
                $stagedPath = Join-Path $stagePath $name
                if ((Test-Path -LiteralPath $path -PathType Leaf) -and
                    (Get-NormalizedHash $path) -eq
                        (Get-NormalizedHash $stagedPath)) {
                    Remove-Item -LiteralPath $path -Force
                }
            }
            if (Test-Path -LiteralPath $manifestPath) {
                Remove-Item -LiteralPath $manifestPath -Force
            }
            if ($movedOriginal -and
                (Test-Path -LiteralPath $chainedPath) -and
                -not (Test-Path -LiteralPath $originalPath)) {
                Move-Item -LiteralPath $chainedPath `
                    -Destination $originalPath
            }
            if ((Test-Path -LiteralPath $backupPath) -and
                (Test-Path -LiteralPath $originalPath) -and
                (Get-NormalizedHash $backupPath) -eq
                    (Get-NormalizedHash $originalPath)) {
                Remove-Item -LiteralPath $backupPath -Force
                if (@(Get-ChildItem -LiteralPath $backupDirectory `
                        -Force).Count -eq 0) {
                    Remove-Item -LiteralPath $backupDirectory
                }
            }
            throw
        }
        Write-Host (
            "Farever Translate $($payloadManifest.ProductVersion) " +
            "fue instalado correctamente en modo $installMode.")
    }
}
finally {
    if (Test-Path -LiteralPath $stagePath) {
        Remove-Item -LiteralPath $stagePath -Recurse -Force
    }
    if (-not $preserveRollback -and
        (Test-Path -LiteralPath $rollbackPath)) {
        Remove-Item -LiteralPath $rollbackPath -Recurse -Force
    }
}
