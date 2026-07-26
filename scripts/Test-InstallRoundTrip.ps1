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
        [string]$BuildContent = 'supported hlboot fixture'
    )

    New-Item -ItemType Directory -Path $Path | Out-Null
    Write-FixtureFile -Path (Join-Path $Path 'Farever.exe') `
        -Content 'farever fixture'
    Write-FixtureFile -Path (Join-Path $Path 'hlboot.dat') `
        -Content $BuildContent
    Write-FixtureFile -Path (Join-Path $Path 'dinput8.dll') `
        -Content $MinimapContent
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

    $minimapName = 'dinput8.dll'
    if (Test-Path -LiteralPath (
            Join-Path $Game 'farever-minimap.dll')) {
        $minimapName = 'farever-minimap.dll'
    }
    $minimapHash =
        (Get-FileHash -LiteralPath (
            Join-Path $Game $minimapName) -Algorithm SHA256).Hash
    $buildHash =
        (Get-FileHash -LiteralPath (
            Join-Path $Game 'hlboot.dat') -Algorithm SHA256).Hash
    & $installScript `
        -GameDirectory $Game `
        -PayloadDirectory $Payload `
        -SupportedMinimapHashes @{ $minimapHash = 'fixture' } `
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
    Assert-True ($manifest.FormatVersion -eq 2) `
        'La instalación no creó un manifiesto v2.'
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
    Assert-True ($migrated.FormatVersion -eq 2) `
        'La actualización no migró el manifiesto v1.'
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

    Write-Host 'Instalación, actualización y desinstalación: OK'
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
