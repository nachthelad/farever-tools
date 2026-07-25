[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $root ('artifacts\install-test-' + [Guid]::NewGuid().ToString('N'))
$game = Join-Path $testRoot 'game'
$payload = Join-Path $testRoot 'payload'

try {
    New-Item -ItemType Directory -Path $game, $payload | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $game 'Farever.exe'), [byte[]](1, 2, 3))
    [IO.File]::WriteAllBytes((Join-Path $game 'hlboot.dat'), [byte[]](4, 5, 6))
    [IO.File]::WriteAllBytes(
        (Join-Path $game 'dinput8.dll'),
        [Text.Encoding]::UTF8.GetBytes('official minimap fixture'))
    $originalHash = (Get-FileHash -LiteralPath (Join-Path $game 'dinput8.dll') -Algorithm SHA256).Hash

    $payloadNames = @(
        'dinput8.dll',
        'FareverTranslate.Host.exe',
        'FareverTranslate.Host.dll',
        'FareverTranslate.Host.deps.json',
        'FareverTranslate.Host.runtimeconfig.json',
        'FareverTranslate.Core.dll'
    )
    foreach ($name in $payloadNames) {
        [IO.File]::WriteAllBytes(
            (Join-Path $payload $name),
            [Text.Encoding]::UTF8.GetBytes("fixture:$name"))
    }

    & (Join-Path $PSScriptRoot 'Install-FareverTranslate.ps1') `
        -GameDirectory $game `
        -PayloadDirectory $payload `
        -ExpectedMinimapHash $originalHash
    if (-not (Test-Path -LiteralPath (Join-Path $game 'farever-translate.manifest.json'))) {
        throw 'La instalación de prueba no creó el manifiesto.'
    }
    if ((Get-FileHash -LiteralPath (Join-Path $game 'farever-minimap.dll') -Algorithm SHA256).Hash -ne $originalHash) {
        throw 'La instalación de prueba alteró el minimapa.'
    }

    $tamperedHost = Join-Path $game 'FareverTranslate.Host.dll'
    [IO.File]::WriteAllBytes(
        $tamperedHost,
        [Text.Encoding]::UTF8.GetBytes('tampered fixture'))
    $tamperRejected = $false
    try {
        & (Join-Path $PSScriptRoot 'Uninstall-FareverTranslate.ps1') `
            -GameDirectory $game
    }
    catch {
        $tamperRejected = $true
    }
    if (-not $tamperRejected -or
        -not (Test-Path -LiteralPath (Join-Path $game 'dinput8.dll')) -or
        -not (Test-Path -LiteralPath (Join-Path $game 'farever-minimap.dll'))) {
        throw 'La desinstalación no abortó de forma segura ante un archivo modificado.'
    }
    Copy-Item -LiteralPath (Join-Path $payload 'FareverTranslate.Host.dll') `
        -Destination $tamperedHost -Force

    & (Join-Path $PSScriptRoot 'Uninstall-FareverTranslate.ps1') `
        -GameDirectory $game
    if ((Get-FileHash -LiteralPath (Join-Path $game 'dinput8.dll') -Algorithm SHA256).Hash -ne $originalHash) {
        throw 'La desinstalación no restauró el hash original.'
    }
    if (Test-Path -LiteralPath (Join-Path $game 'farever-translate.manifest.json')) {
        throw 'La desinstalación dejó el manifiesto.'
    }

    Write-Host 'Instalación y desinstalación de prueba: OK'
}
finally {
    $resolvedArtifacts = (Resolve-Path -LiteralPath (Join-Path $root 'artifacts')).Path
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTest = (Resolve-Path -LiteralPath $testRoot).Path
        if (-not $resolvedTest.StartsWith(
                $resolvedArtifacts + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Se rechazó limpiar una ruta fuera de artifacts.'
        }
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force
    }
}
