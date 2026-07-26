[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudio)) {
    throw 'Falta Visual Studio Build Tools con C++ x64.'
}
$msbuild = Join-Path $visualStudio 'MSBuild\Current\Bin\MSBuild.exe'

$taskPath = $env:PATH
Remove-Item Env:PATH
$env:Path = $taskPath
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.nuget-packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

& $msbuild (Join-Path $root 'native\FareverTranslate.Proxy\FareverTranslate.Proxy.vcxproj') `
    /m /p:Configuration=Release /p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    throw 'Falló el proxy.'
}
& $msbuild (Join-Path $root 'tests\FareverTranslate.FakeDInput\FareverTranslate.FakeDInput.vcxproj') `
    /m /p:Configuration=Release /p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    throw 'Falló el DirectInput simulado.'
}

dotnet build (Join-Path $root 'src\FareverTranslate.Host\FareverTranslate.Host.csproj') `
    -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'Falló el host.'
}
dotnet build (Join-Path $root 'tests\FareverTranslate.Tests\FareverTranslate.Tests.csproj') `
    -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'Fallaron las pruebas al compilar.'
}

$env:FAREVER_PROXY_PATH = Join-Path $root 'artifacts\proxy\Release\dinput8.dll'
$env:FAREVER_FAKE_DINPUT_PATH =
    Join-Path $root 'artifacts\tests\fake-dinput\farever-minimap.dll'
dotnet run `
    --project (Join-Path $root 'tests\FareverTranslate.Tests\FareverTranslate.Tests.csproj') `
    -c Release --no-build
if ($LASTEXITCODE -ne 0) {
    throw 'Fallaron las pruebas automatizadas.'
}

& (Join-Path $PSScriptRoot 'Test-InstallRoundTrip.ps1')
& (Join-Path $PSScriptRoot 'Build-Package.ps1')
& (Join-Path $PSScriptRoot 'Test-Package.ps1')
& (Join-Path $PSScriptRoot 'Test-RepositoryHygiene.ps1')
Write-Host 'Suite completa: OK'
