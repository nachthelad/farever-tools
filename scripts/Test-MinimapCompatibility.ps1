[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$ExpectedSha256
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $Path).Path
$actual = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).
    Hash.ToUpperInvariant()
if ($actual -ne $ExpectedSha256.ToUpperInvariant()) {
    throw "Hash inesperado para el minimapa: $actual"
}

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
$headers = & $dumpbin /headers $resolved | Out-String
if ($headers -notmatch '8664 machine \(x64\)') {
    throw 'El minimapa no es un DLL x64.'
}
$exports = & $dumpbin /exports $resolved | Out-String
foreach ($name in @(
        'DirectInput8Create',
        'DllCanUnloadNow',
        'DllGetClassObject',
        'DllRegisterServer',
        'DllUnregisterServer')) {
    if ($exports -notmatch [regex]::Escape($name)) {
        throw "El minimapa no exporta $name."
    }
}

Write-Host "Minimapa compatible: $actual"
