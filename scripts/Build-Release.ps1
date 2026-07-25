[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$package = Join-Path $artifacts 'package'
$proxyProject = Join-Path $root 'native\FareverTranslate.Proxy\FareverTranslate.Proxy.vcxproj'
$hostProject = Join-Path $root 'src\FareverTranslate.Host\FareverTranslate.Host.csproj'
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'

if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw 'No se encontró Visual Studio Build Tools.'
}
$visualStudio = & $vswhere -latest -products '*' `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudio)) {
    throw 'Falta el componente C++ x64 de Visual Studio Build Tools.'
}
$msbuild = Join-Path $visualStudio 'MSBuild\Current\Bin\MSBuild.exe'

$taskPath = $env:PATH
Remove-Item Env:PATH
$env:Path = $taskPath
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.nuget-packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

& $msbuild $proxyProject /m /p:Configuration=Release /p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    throw 'Falló la compilación del proxy.'
}

dotnet publish $hostProject -c Release -r win-x64 --self-contained false -o $package
if ($LASTEXITCODE -ne 0) {
    throw 'Falló la publicación del host.'
}

Copy-Item -LiteralPath (Join-Path $artifacts 'proxy\Release\dinput8.dll') `
    -Destination (Join-Path $package 'dinput8.dll') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-FareverTranslate.ps1') `
    -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall-FareverTranslate.ps1') `
    -Destination $package -Force

Write-Host "Paquete listo en $package"
