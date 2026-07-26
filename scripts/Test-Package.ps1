[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (
    Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
$packageName = "FareverTranslate-v$version-win-x64"
$package = Join-Path $root "artifacts\package\$packageName"
$archive = Join-Path $root "artifacts\release\$packageName.zip"
$checksums = Join-Path $root 'artifacts\release\SHA256SUMS.txt'

function Assert-True {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

foreach ($path in @($package, $archive, $checksums)) {
    Assert-True (Test-Path -LiteralPath $path) `
        "Falta un artefacto: $path"
}

$expectedFiles = @(
    'Desinstalar.cmd',
    'dinput8.dll',
    'FareverTranslate.Host.exe',
    'Instalar.cmd',
    'LEEME.txt',
    'LICENSE.txt',
    'payload.json',
    'tools\Install-FareverTranslate.ps1',
    'tools\InstallSupport.ps1',
    'tools\Uninstall-FareverTranslate.ps1'
)
$actualFiles = @(Get-ChildItem -LiteralPath $package -Recurse -File |
    ForEach-Object {
        $_.FullName.Substring($package.Length + 1)
    })
$difference = @(Compare-Object $expectedFiles $actualFiles)
Assert-True ($difference.Count -eq 0) `
    "El contenido del paquete es inesperado: $($difference | Out-String)"

$forbidden = @($actualFiles | Where-Object {
    $_ -match '\.(pdb|log|dmp)$' -or
    $_ -ieq 'farever-minimap.dll' -or
    $_ -match 'cache|secret|apikey|chat'
})
Assert-True ($forbidden.Count -eq 0) `
    "El paquete contiene archivos prohibidos: $($forbidden -join ', ')"

. (Join-Path $PSScriptRoot 'InstallSupport.ps1')
$payload = Read-PayloadManifest -PayloadDirectory $package
Assert-True ($payload.ProductVersion -eq $version) `
    'payload.json no coincide con la versión del proyecto.'

$hostExecutable =
    Join-Path $package 'FareverTranslate.Host.exe'
$versionOutput = (& $hostExecutable --version | Out-String).Trim()
Assert-True ($LASTEXITCODE -eq 0) `
    'FareverTranslate.Host.exe --version falló.'
Assert-True ($versionOutput -eq $version) `
    "El host informó una versión incorrecta: $versionOutput"

$proxy = Join-Path $package 'dinput8.dll'
$proxyVersion = (Get-Item -LiteralPath $proxy).VersionInfo
Assert-True ($proxyVersion.ProductVersion -eq $version) `
    'El proxy no contiene la versión pública correcta.'

$vswhere =
    'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
$msvc = Get-ChildItem -LiteralPath (
    Join-Path $visualStudio 'VC\Tools\MSVC') -Directory |
    Sort-Object Name -Descending |
    Select-Object -First 1
$dumpbin = Join-Path $msvc.FullName 'bin\Hostx64\x64\dumpbin.exe'
$dependencies = & $dumpbin /dependents $proxy | Out-String
Assert-True (
    $dependencies -notmatch 'MSVCP\d+\.dll|VCRUNTIME\d*.*\.dll') `
    'El proxy todavía depende del Visual C++ Redistributable.'

$exports = & $dumpbin /exports $proxy | Out-String
foreach ($name in @(
        'DirectInput8Create',
        'DllCanUnloadNow',
        'DllGetClassObject',
        'DllRegisterServer',
        'DllUnregisterServer')) {
    Assert-True ($exports -match [regex]::Escape($name)) `
        "Falta el export $name."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $zipFiles = @($zip.Entries |
        Where-Object { -not [string]::IsNullOrEmpty($_.Name) } |
        ForEach-Object { $_.FullName -replace '/', '\' })
}
finally {
    $zip.Dispose()
}
$expectedZipFiles = @($expectedFiles | ForEach-Object {
    "$packageName\$_"
})
$zipDifference = @(Compare-Object $expectedZipFiles $zipFiles)
Assert-True ($zipDifference.Count -eq 0) `
    "El ZIP no coincide con staging: $($zipDifference | Out-String)"

$archiveHash =
    (Get-FileHash -LiteralPath $archive -Algorithm SHA256).
        Hash.ToUpperInvariant()
$checksumText = Get-Content -LiteralPath $checksums -Raw
Assert-True (
    $checksumText.Trim() -eq
        "$archiveHash *$([IO.Path]::GetFileName($archive))") `
    'SHA256SUMS.txt no coincide con el ZIP.'

Assert-True (
    Test-WindowsDesktopRuntimeInventory @(
        'Microsoft.WindowsDesktop.App 8.0.19 [C:\Program Files\dotnet]')) `
    'La detección no reconoció .NET Desktop 8.'
Assert-True (
    -not (Test-WindowsDesktopRuntimeInventory @(
        'Microsoft.NETCore.App 8.0.19 [C:\Program Files\dotnet]'))) `
    'La detección confundió .NET Core con Desktop Runtime.'

$vdfPath = Join-Path $root 'artifacts\package-libraryfolders.vdf'
try {
    [IO.File]::WriteAllText(
        $vdfPath,
        @'
"libraryfolders"
{
  "0" { "path" "C:\\Program Files (x86)\\Steam" }
  "1" { "path" "E:\\SteamLibrary" }
}
'@,
        [Text.UTF8Encoding]::new($false))
    $libraries = @(Get-SteamLibraryPathsFromVdf -Path $vdfPath)
    Assert-True ($libraries -contains 'E:\SteamLibrary') `
        'No se interpretó una biblioteca Steam secundaria.'
}
finally {
    if (Test-Path -LiteralPath $vdfPath) {
        Remove-Item -LiteralPath $vdfPath -Force
    }
}

$installerText = Get-Content -LiteralPath (
    Join-Path $PSScriptRoot 'Install-FareverTranslate.ps1') -Raw
foreach ($hash in @(
        '38888C303AD65F17E2C3B398D6B723750E4B694095A02FDE786DEF10C08F83B6',
        '2C22ECE52A9F7583AC2AA694BD91BC0B4488A69F216C0C249B4F15422D4BFC66',
        '7C3CA4DD765B50972B0EB524F82F9943A1D24D9EA52CBBA97C7F87D5D840E7A0')) {
    Assert-True ($installerText.Contains($hash)) `
        "El instalador no contiene el hash compatible $hash."
}

Write-Host 'Contrato del paquete portable: OK'
