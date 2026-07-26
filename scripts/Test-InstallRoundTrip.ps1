[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $root (
    'artifacts\install-test-' + [Guid]::NewGuid().ToString('N'))
$installScript =
    Join-Path $PSScriptRoot 'Install-FareverTranslate.ps1'
$uninstallScript =
    Join-Path $PSScriptRoot 'Uninstall-FareverTranslate.ps1'

function Write-FixtureFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    [IO.File]::WriteAllBytes(
        $Path,
        [Text.Encoding]::UTF8.GetBytes($Content))
}

function New-FixtureGame {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$MinimapContent = 'official minimap fixture',
        [string]$BuildContent = 'supported hlboot fixture',
        [switch]$WithoutDinput
    )

    New-Item -ItemType Directory -Path $Path | Out-Null
    Write-FixtureFile -Path (Join-Path $Path 'Farever.exe') `
        -Content 'farever fixture'
    Write-FixtureFile -Path (Join-Path $Path 'hlboot.dat') `
        -Content $BuildContent
    if (-not $WithoutDinput) {
        Write-FixtureFile -Path (Join-Path $Path 'dinput8.dll') `
            -Content $MinimapContent
    }
}

function New-FixturePayload {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Content
    )

    New-Item -ItemType Directory -Path $Path | Out-Null
    $files = foreach ($name in @(
            'dinput8.dll',
            'FareverTranslate.Host.exe')) {
        $filePath = Join-Path $Path $name
        Write-FixtureFile -Path $filePath `
            -Content "$Content`:$name"
        [ordered]@{
            Name = $name
            Sha256 = (Get-FileHash -LiteralPath $filePath `
                -Algorithm SHA256).Hash
        }
    }
    $payload = [ordered]@{
        FormatVersion = 1
        ProductVersion = $Version
        Files = @($files)
    }
    [IO.File]::WriteAllText(
        (Join-Path $Path 'payload.json'),
        ($payload | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))
}

function Invoke-FixtureInstall {
    param(
        [Parameter(Mandatory)][string]$Game,
        [Parameter(Mandatory)][string]$Payload,
        [int]$FailAfter
    )

    $minimapHashes = @{}
    foreach ($minimapName in @(
            'farever-minimap.dll',
            'dinput8.dll')) {
        $minimapPath = Join-Path $Game $minimapName
        if (Test-Path -LiteralPath $minimapPath -PathType Leaf) {
            $minimapHash =
                (Get-FileHash -LiteralPath $minimapPath `
                    -Algorithm SHA256).Hash
            $minimapHashes[$minimapHash] = 'fixture'
            break
        }
    }
    $buildHash =
        (Get-FileHash -LiteralPath (
            Join-Path $Game 'hlboot.dat') -Algorithm SHA256).Hash
    & $installScript `
        -GameDirectory $Game `
        -PayloadDirectory $Payload `
        -SupportedMinimapHashes $minimapHashes `
        -SupportedGameBuildHashes @($buildHash) `
        -NoFolderPicker `
        -SkipRuntimeCheck `
        -TestFailAfterReplacements $FailAfter
}

function Assert-True {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $game = Join-Path $testRoot 'game'
    $payload1 = Join-Path $testRoot 'payload-1'
    $payload2 = Join-Path $testRoot 'payload-2'
    $payload3 = Join-Path $testRoot 'payload-3'
    New-FixtureGame -Path $game
    New-FixturePayload -Path $payload1 `
        -Version '0.1.0-beta.1' -Content 'payload one'
    New-FixturePayload -Path $payload2 `
        -Version '0.1.0-beta.2' -Content 'payload two'
    New-FixturePayload -Path $payload3 `
        -Version '0.1.0-beta.3' -Content 'payload three'
    $originalHash =
        (Get-FileHash -LiteralPath (
            Join-Path $game 'dinput8.dll') -Algorithm SHA256).Hash

    Invoke-FixtureInstall -Game $game -Payload $payload1
    $manifestPath =
        Join-Path $game 'farever-translate.manifest.json'
    $manifest =
        Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-True ($manifest.FormatVersion -eq 3) `
        'La instalación no creó un manifiesto v3.'
    Assert-True ($manifest.InstallMode -eq 'ChainedMinimap') `
        'La instalación no registró el modo encadenado.'
    Assert-True ($manifest.ProductVersion -eq '0.1.0-beta.1') `
        'La instalación registró una versión incorrecta.'
    Assert-True (
        (Get-FileHash -LiteralPath (
            Join-Path $game 'farever-minimap.dll') `
            -Algorithm SHA256).Hash -eq $originalHash) `
        'La instalación alteró el minimapa encadenado.'
    Assert-True (
        (Get-FileHash -LiteralPath (
            Join-Path $game (
                'FareverTranslate.Backup\dinput8.original.dll')) `
            -Algorithm SHA256).Hash -eq $originalHash) `
        'La instalación alteró el respaldo.'

    $tamperedHost =
        Join-Path $game 'FareverTranslate.Host.exe'
    Write-FixtureFile -Path $tamperedHost -Content 'tampered fixture'
    $tamperRejected = $false
    try {
        & $uninstallScript -GameDirectory $game -NoFolderPicker
    }
    catch {
        $tamperRejected = $true
    }
    Assert-True $tamperRejected `
        'La desinstalación aceptó un archivo manipulado.'
    Assert-True (Test-Path -LiteralPath (
        Join-Path $game 'farever-minimap.dll')) `
        'El rechazo por manipulación modificó el minimapa.'
    Copy-Item -LiteralPath (
        Join-Path $payload1 'FareverTranslate.Host.exe') `
        -Destination $tamperedHost -Force

    Invoke-FixtureInstall -Game $game -Payload $payload2
    $manifest =
        Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-True ($manifest.ProductVersion -eq '0.1.0-beta.2') `
        'La actualización no registró la versión nueva.'
    $beforeRollback = @{}
    foreach ($file in $manifest.Files) {
        $beforeRollback[[string]$file.Name] =
            (Get-FileHash -LiteralPath (
                Join-Path $game ([string]$file.Name)) `
                -Algorithm SHA256).Hash
    }

    $rollbackTriggered = $false
    try {
        Invoke-FixtureInstall -Game $game `
            -Payload $payload3 -FailAfter 1
    }
    catch {
        $rollbackTriggered = $true
    }
    Assert-True $rollbackTriggered `
        'La prueba no activó el rollback.'
    $manifest =
        Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-True ($manifest.ProductVersion -eq '0.1.0-beta.2') `
        'El rollback no restauró el manifiesto anterior.'
    foreach ($name in $beforeRollback.Keys) {
        Assert-True (
            (Get-FileHash -LiteralPath (
                Join-Path $game $name) -Algorithm SHA256).Hash -eq
                $beforeRollback[$name]) `
            "El rollback no restauró $name."
    }
    Assert-True (
        @(Get-ChildItem -LiteralPath $game -Force |
            Where-Object Name -like '.farever-translate-*').Count -eq 0) `
        'La actualización dejó carpetas temporales.'

    & $uninstallScript -GameDirectory $game -NoFolderPicker
    Assert-True (
        (Get-FileHash -LiteralPath (
            Join-Path $game 'dinput8.dll') `
            -Algorithm SHA256).Hash -eq $originalHash) `
        'La desinstalación no restauró el DLL original.'
    Assert-True (-not (Test-Path -LiteralPath $manifestPath)) `
        'La desinstalación dejó el manifiesto.'

    $standaloneGame = Join-Path $testRoot 'standalone-game'
    New-FixtureGame -Path $standaloneGame -WithoutDinput
    Invoke-FixtureInstall -Game $standaloneGame -Payload $payload1
    $standaloneManifestPath =
        Join-Path $standaloneGame 'farever-translate.manifest.json'
    $standaloneManifest =
        Get-Content -LiteralPath $standaloneManifestPath -Raw |
        ConvertFrom-Json
    Assert-True ($standaloneManifest.FormatVersion -eq 3) `
        'La instalación independiente no creó un manifiesto v3.'
    Assert-True ($standaloneManifest.InstallMode -eq 'Standalone') `
        'La instalación independiente registró un modo incorrecto.'
    foreach ($name in @(
            'MinimapVersion',
            'OriginalDinput8Sha256',
            'ChainedFile',
            'BackupFile')) {
        Assert-True ($null -eq $standaloneManifest.$name) `
            "El manifiesto independiente registró $name."
    }
    Assert-True (Test-Path -LiteralPath (
        Join-Path $standaloneGame 'dinput8.dll')) `
        'La instalación independiente no copió el proxy.'
    Assert-True (-not (Test-Path -LiteralPath (
        Join-Path $standaloneGame 'farever-minimap.dll'))) `
        'La instalación independiente creó un minimapa ficticio.'
    Assert-True (-not (Test-Path -LiteralPath (
        Join-Path $standaloneGame 'FareverTranslate.Backup'))) `
        'La instalación independiente creó un respaldo ficticio.'

    $standaloneHost =
        Join-Path $standaloneGame 'FareverTranslate.Host.exe'
    Write-FixtureFile -Path $standaloneHost `
        -Content 'tampered standalone fixture'
    $standaloneTamperRejected = $false
    try {
        & $uninstallScript `
            -GameDirectory $standaloneGame -NoFolderPicker
    }
    catch {
        $standaloneTamperRejected = $true
    }
    Assert-True $standaloneTamperRejected `
        'La desinstalación independiente aceptó un archivo manipulado.'
    Assert-True (Test-Path -LiteralPath (
        Join-Path $standaloneGame 'dinput8.dll')) `
        'El rechazo independiente eliminó el proxy.'
    Copy-Item -LiteralPath (
        Join-Path $payload1 'FareverTranslate.Host.exe') `
        -Destination $standaloneHost -Force

    Invoke-FixtureInstall -Game $standaloneGame -Payload $payload2
    $standaloneManifest =
        Get-Content -LiteralPath $standaloneManifestPath -Raw |
        ConvertFrom-Json
    Assert-True (
        $standaloneManifest.ProductVersion -eq '0.1.0-beta.2') `
        'La actualización independiente no registró la versión nueva.'
    Assert-True ($standaloneManifest.InstallMode -eq 'Standalone') `
        'La actualización independiente cambió de modo.'
    $standaloneBeforeRollback = @{}
    foreach ($file in $standaloneManifest.Files) {
        $standaloneBeforeRollback[[string]$file.Name] =
            (Get-FileHash -LiteralPath (
                Join-Path $standaloneGame ([string]$file.Name)) `
                -Algorithm SHA256).Hash
    }

    $standaloneRollbackTriggered = $false
    try {
        Invoke-FixtureInstall -Game $standaloneGame `
            -Payload $payload3 -FailAfter 1
    }
    catch {
        $standaloneRollbackTriggered = $true
    }
    Assert-True $standaloneRollbackTriggered `
        'La prueba independiente no activó el rollback.'
    $standaloneManifest =
        Get-Content -LiteralPath $standaloneManifestPath -Raw |
        ConvertFrom-Json
    Assert-True (
        $standaloneManifest.ProductVersion -eq '0.1.0-beta.2') `
        'El rollback independiente no restauró el manifiesto.'
    foreach ($name in $standaloneBeforeRollback.Keys) {
        Assert-True (
            (Get-FileHash -LiteralPath (
                Join-Path $standaloneGame $name) `
                -Algorithm SHA256).Hash -eq
                $standaloneBeforeRollback[$name]) `
            "El rollback independiente no restauró $name."
    }

    & $uninstallScript `
        -GameDirectory $standaloneGame -NoFolderPicker
    foreach ($name in @(
            'dinput8.dll',
            'FareverTranslate.Host.exe',
            'farever-translate.manifest.json',
            'farever-minimap.dll',
            'FareverTranslate.Backup')) {
        Assert-True (-not (Test-Path -LiteralPath (
            Join-Path $standaloneGame $name))) `
            "La desinstalación independiente dejó $name."
    }

    $legacyGame = Join-Path $testRoot 'legacy-game'
    New-FixtureGame -Path $legacyGame
    Invoke-FixtureInstall -Game $legacyGame -Payload $payload1
    $legacyManifestPath =
        Join-Path $legacyGame 'farever-translate.manifest.json'
    $installed =
        Get-Content -LiteralPath $legacyManifestPath -Raw |
        ConvertFrom-Json
    $legacyFiles = [Collections.Generic.List[object]]::new()
    foreach ($file in $installed.Files) {
        $legacyFiles.Add($file)
    }
    foreach ($name in @(
            'FareverTranslate.Host.dll',
            'FareverTranslate.Host.deps.json',
            'FareverTranslate.Host.runtimeconfig.json',
            'FareverTranslate.Core.dll')) {
        $path = Join-Path $legacyGame $name
        Write-FixtureFile -Path $path -Content "legacy:$name"
        $legacyFiles.Add([ordered]@{
            Name = $name
            Sha256 = (Get-FileHash -LiteralPath $path `
                -Algorithm SHA256).Hash
        })
    }
    $legacy = [ordered]@{
        FormatVersion = 1
        InstalledAtUtc = [string]$installed.InstalledAtUtc
        OriginalDinput8Sha256 =
            [string]$installed.OriginalDinput8Sha256
        ChainedFile = 'farever-minimap.dll'
        BackupFile =
            'FareverTranslate.Backup\dinput8.original.dll'
        Files = @($legacyFiles)
    }
    [IO.File]::WriteAllText(
        $legacyManifestPath,
        ($legacy | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))

    Invoke-FixtureInstall -Game $legacyGame -Payload $payload2
    $migrated =
        Get-Content -LiteralPath $legacyManifestPath -Raw |
        ConvertFrom-Json
    Assert-True ($migrated.FormatVersion -eq 3) `
        'La actualización no migró el manifiesto v1.'
    Assert-True ($migrated.InstallMode -eq 'ChainedMinimap') `
        'La migración v1 no conservó el modo encadenado.'
    Assert-True ($migrated.ProductVersion -eq '0.1.0-beta.2') `
        'La migración registró una versión incorrecta.'
    foreach ($name in @(
            'FareverTranslate.Host.dll',
            'FareverTranslate.Host.deps.json',
            'FareverTranslate.Host.runtimeconfig.json',
            'FareverTranslate.Core.dll')) {
        Assert-True (-not (Test-Path -LiteralPath (
            Join-Path $legacyGame $name))) `
            "La migración no eliminó el archivo obsoleto $name."
    }
    & $uninstallScript -GameDirectory $legacyGame -NoFolderPicker

    $legacyV2Game = Join-Path $testRoot 'legacy-v2-game'
    New-FixtureGame -Path $legacyV2Game
    Invoke-FixtureInstall -Game $legacyV2Game -Payload $payload1
    $legacyV2ManifestPath =
        Join-Path $legacyV2Game 'farever-translate.manifest.json'
    $legacyV2 =
        Get-Content -LiteralPath $legacyV2ManifestPath -Raw |
        ConvertFrom-Json
    $legacyV2.FormatVersion = 2
    $legacyV2.PSObject.Properties.Remove('InstallMode')
    [IO.File]::WriteAllText(
        $legacyV2ManifestPath,
        ($legacyV2 | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))
    Invoke-FixtureInstall -Game $legacyV2Game -Payload $payload2
    $migratedV2 =
        Get-Content -LiteralPath $legacyV2ManifestPath -Raw |
        ConvertFrom-Json
    Assert-True ($migratedV2.FormatVersion -eq 3) `
        'La actualización no migró el manifiesto v2.'
    Assert-True ($migratedV2.InstallMode -eq 'ChainedMinimap') `
        'La migración v2 no conservó el modo encadenado.'
    & $uninstallScript -GameDirectory $legacyV2Game -NoFolderPicker

    $guardGame = Join-Path $testRoot 'manifest-guard'
    New-FixtureGame -Path $guardGame
    Invoke-FixtureInstall -Game $guardGame -Payload $payload1
    $guardManifestPath =
        Join-Path $guardGame 'farever-translate.manifest.json'
    $guardManifestText =
        Get-Content -LiteralPath $guardManifestPath -Raw
    $guardManifest = $guardManifestText | ConvertFrom-Json
    $guardFiles = @($guardManifest.Files) + @(
        [ordered]@{
            Name = 'Farever.exe'
            Sha256 = (Get-FileHash -LiteralPath (
                Join-Path $guardGame 'Farever.exe') `
                -Algorithm SHA256).Hash
        })
    $guardManifest.Files = $guardFiles
    [IO.File]::WriteAllText(
        $guardManifestPath,
        ($guardManifest | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))
    $unexpectedNameRejected = $false
    try {
        & $uninstallScript -GameDirectory $guardGame -NoFolderPicker
    }
    catch {
        $unexpectedNameRejected = $true
    }
    Assert-True $unexpectedNameRejected `
        'La desinstalación aceptó un archivo ajeno en el manifiesto.'
    Assert-True (Test-Path -LiteralPath (
        Join-Path $guardGame 'Farever.exe')) `
        'El manifiesto manipulado eliminó un archivo ajeno.'
    [IO.File]::WriteAllText(
        $guardManifestPath,
        $guardManifestText,
        [Text.UTF8Encoding]::new($false))
    & $uninstallScript -GameDirectory $guardGame -NoFolderPicker

    $unknownGame = Join-Path $testRoot 'unknown-minimap'
    New-FixtureGame -Path $unknownGame
    $unknownOriginalHash =
        (Get-FileHash -LiteralPath (
            Join-Path $unknownGame 'dinput8.dll') `
            -Algorithm SHA256).Hash
    $unknownRejected = $false
    try {
        $buildHash =
            (Get-FileHash -LiteralPath (
                Join-Path $unknownGame 'hlboot.dat') `
                -Algorithm SHA256).Hash
        & $installScript `
            -GameDirectory $unknownGame `
            -PayloadDirectory $payload1 `
            -SupportedMinimapHashes @{
                ('0' * 64) = 'never'
            } `
            -SupportedGameBuildHashes @($buildHash) `
            -NoFolderPicker `
            -SkipRuntimeCheck
    }
    catch {
        $unknownRejected = $true
    }
    Assert-True $unknownRejected `
        'La instalación aceptó un minimapa desconocido.'
    Assert-True (
        (Get-FileHash -LiteralPath (
            Join-Path $unknownGame 'dinput8.dll') `
            -Algorithm SHA256).Hash -eq
        $unknownOriginalHash) `
        'El rechazo modificó el minimapa desconocido.'
    Assert-True (-not (Test-Path -LiteralPath (
        Join-Path $unknownGame 'farever-translate.manifest.json'))) `
        'El rechazo dejó un manifiesto.'
    Assert-True (-not (Test-Path -LiteralPath (
        Join-Path $unknownGame 'FareverTranslate.Host.exe'))) `
        'El rechazo dejó el host.'
    Assert-True (-not (Test-Path -LiteralPath (
        Join-Path $unknownGame 'farever-minimap.dll'))) `
        'El rechazo movió el DLL desconocido.'
    Assert-True (-not (Test-Path -LiteralPath (
        Join-Path $unknownGame 'FareverTranslate.Backup'))) `
        'El rechazo creó un respaldo.'

    Write-Host (
        'Instalación independiente/encadenada, actualización y ' +
        'desinstalación: OK')
}
finally {
    $artifactsPath = Join-Path $root 'artifacts'
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedArtifacts = (Resolve-Path -LiteralPath $artifactsPath).
            Path.TrimEnd([IO.Path]::DirectorySeparatorChar)
        $resolvedTest = (Resolve-Path -LiteralPath $testRoot).Path
        if (-not $resolvedTest.StartsWith(
                $resolvedArtifacts +
                    [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Se rechazó limpiar una ruta fuera de artifacts.'
        }
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force
    }
}
