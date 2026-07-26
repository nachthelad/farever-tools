[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$propsPath = Join-Path $root 'Directory.Build.props'

[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$projectVersion = [string]$props.Project.PropertyGroup.Version
if (-not [string]::IsNullOrWhiteSpace($Version) -and
    $Version -ne $projectVersion) {
    throw (
        "La versión solicitada $Version no coincide con " +
        "$projectVersion. Actualizá los metadatos antes de empaquetar.")
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $projectVersion
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Versión no válida: $Version"
}

$packageName = "FareverTranslate-v$Version-win-x64"
$packageContainer = Join-Path $artifacts 'package'
$package = Join-Path $packageContainer $packageName
$releaseDirectory = Join-Path $artifacts 'release'
$publishDirectory = Join-Path $artifacts 'publish\win-x64'
$archivePath = Join-Path $releaseDirectory "$packageName.zip"
$checksumPath = Join-Path $releaseDirectory 'SHA256SUMS.txt'

function Reset-ArtifactDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $fullArtifacts = [IO.Path]::GetFullPath($artifacts).
        TrimEnd([IO.Path]::DirectorySeparatorChar)
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith(
            $fullArtifacts + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Se rechazó limpiar una ruta fuera de artifacts: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
}

$vswhere =
    'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw 'No se encontró Visual Studio Build Tools.'
}
$visualStudio = & $vswhere -latest -products '*' `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudio)) {
    throw 'Falta el componente C++ x64 de Visual Studio Build Tools.'
}
$msbuild =
    Join-Path $visualStudio 'MSBuild\Current\Bin\MSBuild.exe'

$taskPath = $env:PATH
Remove-Item Env:PATH
$env:Path = $taskPath
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.nuget-packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Reset-ArtifactDirectory -Path $packageContainer
Reset-ArtifactDirectory -Path $releaseDirectory
Reset-ArtifactDirectory -Path $publishDirectory
New-Item -ItemType Directory -Path $package | Out-Null
New-Item -ItemType Directory -Path (
    Join-Path $package 'tools') | Out-Null

$proxyProject = Join-Path $root (
    'native\FareverTranslate.Proxy\FareverTranslate.Proxy.vcxproj')
& $msbuild $proxyProject /m /p:Configuration=Release /p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    throw 'Falló la compilación del proxy.'
}

$hostProject = Join-Path $root (
    'src\FareverTranslate.Host\FareverTranslate.Host.csproj')
dotnet publish $hostProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    -p:Version=$Version `
    -p:InformationalVersion=$Version `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'Falló la publicación single-file del host.'
}

$hostSource =
    Join-Path $publishDirectory 'FareverTranslate.Host.exe'
$proxySource =
    Join-Path $artifacts 'proxy\Release\dinput8.dll'
foreach ($required in @($hostSource, $proxySource)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Falta un binario del paquete: $required"
    }
}

Copy-Item -LiteralPath $hostSource `
    -Destination (Join-Path $package 'FareverTranslate.Host.exe')
Copy-Item -LiteralPath $proxySource `
    -Destination (Join-Path $package 'dinput8.dll')
Copy-Item -LiteralPath (Join-Path $root 'packaging\Instalar.cmd') `
    -Destination $package
Copy-Item -LiteralPath (Join-Path $root 'packaging\Desinstalar.cmd') `
    -Destination $package
Copy-Item -LiteralPath (Join-Path $root 'packaging\LEEME.txt') `
    -Destination $package
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') `
    -Destination (Join-Path $package 'LICENSE.txt')
Copy-Item -LiteralPath (
    Join-Path $PSScriptRoot 'Install-FareverTranslate.ps1') `
    -Destination (Join-Path $package 'tools')
Copy-Item -LiteralPath (
    Join-Path $PSScriptRoot 'Uninstall-FareverTranslate.ps1') `
    -Destination (Join-Path $package 'tools')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'InstallSupport.ps1') `
    -Destination (Join-Path $package 'tools')

$payloadFiles = foreach ($name in @(
        'dinput8.dll',
        'FareverTranslate.Host.exe')) {
    [ordered]@{
        Name = $name
        Sha256 = (Get-FileHash -LiteralPath (
            Join-Path $package $name) -Algorithm SHA256).
                Hash.ToUpperInvariant()
    }
}
$payload = [ordered]@{
    FormatVersion = 1
    ProductVersion = $Version
    Files = @($payloadFiles)
}
$payloadJson = $payload | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText(
    (Join-Path $package 'payload.json'),
    $payloadJson,
    [Text.UTF8Encoding]::new($false))

Compress-Archive -LiteralPath $package `
    -DestinationPath $archivePath `
    -CompressionLevel Optimal
$archiveHash =
    (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).
        Hash.ToUpperInvariant()
[IO.File]::WriteAllText(
    $checksumPath,
    "$archiveHash *$([IO.Path]::GetFileName($archivePath))`n",
    [Text.UTF8Encoding]::new($false))

Write-Host "Paquete: $archivePath"
Write-Host "SHA-256: $archiveHash"
