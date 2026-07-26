Set-StrictMode -Version Latest

$script:FareverAppId = '3672400'
$script:DotNetRuntimeUrl =
    'https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime'

function Get-NormalizedHash {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).
        Hash.ToUpperInvariant()
}

function Assert-LeafName {
    param([Parameter(Mandatory)][string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name) -or
        [IO.Path]::GetFileName($Name) -ne $Name) {
        throw "Nombre de archivo no permitido: $Name"
    }
}

function Test-Sha256 {
    param([Parameter(Mandatory)][string]$Value)

    return $Value -match '^[0-9A-Fa-f]{64}$'
}

function Test-WindowsDesktopRuntimeInventory {
    param([AllowEmptyCollection()][string[]]$Inventory)

    return [bool]($Inventory | Where-Object {
        $_ -match '^Microsoft\.WindowsDesktop\.App 8\.\d+\.\d+ \['
    } | Select-Object -First 1)
}

function Assert-WindowsDesktopRuntime {
    $dotnetCandidates = @(
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        (Get-Command dotnet.exe -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty Source -First 1)
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique

    foreach ($dotnet in $dotnetCandidates) {
        if ((Test-Path -LiteralPath $dotnet -PathType Leaf) -and
            (Test-WindowsDesktopRuntimeInventory (& $dotnet --list-runtimes))) {
            return
        }
    }

    throw (
        "Falta .NET 8 Desktop Runtime x64.`n" +
        "Descargalo desde: $script:DotNetRuntimeUrl")
}

function Get-SteamLibraryPathsFromVdf {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return @()
    }

    $content = Get-Content -LiteralPath $Path -Raw
    $matches = [regex]::Matches(
        $content,
        '"path"\s+"(?<path>(?:\\.|[^"])*)"')
    $paths = foreach ($match in $matches) {
        $value = $match.Groups['path'].Value
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $value -replace '\\\\', '\'
        }
    }
    return @($paths | Select-Object -Unique)
}

function Get-SteamRootPaths {
    $paths = [Collections.Generic.List[string]]::new()
    $registryLocations = @(
        @{ Path = 'HKCU:\Software\Valve\Steam'; Names = @('SteamPath') },
        @{ Path = 'HKLM:\Software\Valve\Steam'; Names = @('InstallPath') },
        @{
            Path = 'HKLM:\Software\WOW6432Node\Valve\Steam'
            Names = @('InstallPath')
        }
    )

    foreach ($location in $registryLocations) {
        $item = Get-ItemProperty -LiteralPath $location.Path `
            -ErrorAction SilentlyContinue
        if ($null -eq $item) {
            continue
        }
        foreach ($name in $location.Names) {
            $value = [string]$item.$name
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                $paths.Add($value)
            }
        }
    }

    return @($paths | Select-Object -Unique)
}

function Get-SteamFareverCandidates {
    $libraryPaths = [Collections.Generic.List[string]]::new()
    foreach ($steamRoot in Get-SteamRootPaths) {
        $libraryPaths.Add($steamRoot)
        $vdf = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
        foreach ($library in Get-SteamLibraryPathsFromVdf -Path $vdf) {
            $libraryPaths.Add($library)
        }
    }

    $candidates = foreach ($library in $libraryPaths) {
        Join-Path $library 'steamapps\common\Farever'
    }
    return @($candidates | Select-Object -Unique)
}

function Test-FareverDirectory {
    param(
        [Parameter(Mandatory)][string]$Path,
        [switch]$RequireManifest
    )

    $executablePath = Join-Path $Path 'Farever.exe'
    $manifestPath =
        Join-Path $Path 'farever-translate.manifest.json'
    if (-not (Test-Path -LiteralPath $Path -PathType Container) -or
        -not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        return $false
    }
    if ($RequireManifest -and
        -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        return $false
    }
    return $true
}

function Test-FareverRunningInDirectory {
    param([Parameter(Mandatory)][string]$GameDirectory)

    $target = [IO.Path]::GetFullPath($GameDirectory).
        TrimEnd([IO.Path]::DirectorySeparatorChar)
    foreach ($process in Get-Process -Name Farever `
            -ErrorAction SilentlyContinue) {
        try {
            $processPath = $process.Path
        }
        catch {
            return $true
        }
        if ([string]::IsNullOrWhiteSpace($processPath)) {
            return $true
        }
        $processDirectory =
            [IO.Path]::GetDirectoryName(
                [IO.Path]::GetFullPath($processPath)).
                TrimEnd([IO.Path]::DirectorySeparatorChar)
        if ([string]::Equals(
                $processDirectory,
                $target,
                [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Select-FareverDirectory {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = [Windows.Forms.FolderBrowserDialog]::new()
    try {
        $dialog.Description =
            'Seleccioná la carpeta que contiene Farever.exe'
        $dialog.ShowNewFolderButton = $false
        if ($dialog.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) {
            return $null
        }
        return $dialog.SelectedPath
    }
    finally {
        $dialog.Dispose()
    }
}

function Find-FareverGameDirectory {
    param(
        [string]$GameDirectory,
        [string]$StartingDirectory,
        [switch]$RequireManifest,
        [switch]$NoFolderPicker
    )

    if (-not [string]::IsNullOrWhiteSpace($GameDirectory)) {
        if (-not (Test-FareverDirectory -Path $GameDirectory `
                -RequireManifest:$RequireManifest)) {
            throw "La carpeta indicada no contiene una instalación válida: $GameDirectory"
        }
        return (Resolve-Path -LiteralPath $GameDirectory).Path
    }

    $candidates = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($StartingDirectory)) {
        $candidates.Add($StartingDirectory)
    }
    foreach ($candidate in Get-SteamFareverCandidates) {
        $candidates.Add($candidate)
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-FareverDirectory -Path $candidate `
                -RequireManifest:$RequireManifest) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    if (-not $NoFolderPicker) {
        $selected = Select-FareverDirectory
        if (-not [string]::IsNullOrWhiteSpace($selected) -and
            (Test-FareverDirectory -Path $selected `
                -RequireManifest:$RequireManifest)) {
            return (Resolve-Path -LiteralPath $selected).Path
        }
    }

    throw 'No se encontró la carpeta de Farever.'
}

function Write-JsonAtomic {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Value
    )

    $temporary = "$Path.tmp"
    $json = $Value | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText(
        $temporary,
        $json,
        [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}

function Read-PayloadManifest {
    param([Parameter(Mandatory)][string]$PayloadDirectory)

    $path = Join-Path $PayloadDirectory 'payload.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'El paquete no contiene payload.json.'
    }
    $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($manifest.FormatVersion -ne 1 -or
        [string]$manifest.ProductVersion -notmatch
            '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
        throw 'El manifiesto del paquete no es compatible.'
    }

    $expectedNames = @('dinput8.dll', 'FareverTranslate.Host.exe')
    $actualNames = @($manifest.Files | ForEach-Object {
        [string]$_.Name
    })
    if ($actualNames.Count -ne $expectedNames.Count -or
        (Compare-Object $expectedNames $actualNames)) {
        throw 'La lista de binarios del paquete es inesperada.'
    }

    foreach ($file in $manifest.Files) {
        $name = [string]$file.Name
        $expectedHash = ([string]$file.Sha256).ToUpperInvariant()
        Assert-LeafName $name
        if (-not (Test-Sha256 $expectedHash)) {
            throw "Hash no válido en payload.json: $name"
        }
        $filePath = Join-Path $PayloadDirectory $name
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf) -or
            (Get-NormalizedHash $filePath) -ne $expectedHash) {
            throw "El archivo del paquete no coincide: $name"
        }
    }

    return $manifest
}

function Read-ValidatedInstallation {
    param([Parameter(Mandatory)][string]$GameDirectory)

    $manifestPath =
        Join-Path $GameDirectory 'farever-translate.manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw 'No se encontró el manifiesto de Farever Translate.'
    }
    $manifest =
        Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.FormatVersion -notin @(1, 2)) {
        throw 'El formato del manifiesto instalado no es compatible.'
    }

    $expectedOriginal =
        ([string]$manifest.OriginalDinput8Sha256).ToUpperInvariant()
    if (-not (Test-Sha256 $expectedOriginal)) {
        throw 'El hash original del manifiesto no es válido.'
    }
    if ([string]$manifest.ChainedFile -ne 'farever-minimap.dll' -or
        [string]$manifest.BackupFile -ne
            'FareverTranslate.Backup\dinput8.original.dll') {
        throw 'Las rutas del manifiesto instalado son inesperadas.'
    }

    $chainedPath = Join-Path $GameDirectory 'farever-minimap.dll'
    $backupPath = Join-Path $GameDirectory (
        'FareverTranslate.Backup\dinput8.original.dll')
    foreach ($path in @($chainedPath, $backupPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-NormalizedHash $path) -ne $expectedOriginal) {
            throw 'El minimapa o su respaldo fueron modificados.'
        }
    }

    $names = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $allowedNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($allowedName in @(
            'dinput8.dll',
            'FareverTranslate.Host.exe',
            'FareverTranslate.Host.dll',
            'FareverTranslate.Host.deps.json',
            'FareverTranslate.Host.runtimeconfig.json',
            'FareverTranslate.Core.dll')) {
        $null = $allowedNames.Add($allowedName)
    }
    foreach ($file in $manifest.Files) {
        $name = [string]$file.Name
        $expectedHash = ([string]$file.Sha256).ToUpperInvariant()
        Assert-LeafName $name
        if (-not $allowedNames.Contains($name) -or
            -not $names.Add($name) -or
            -not (Test-Sha256 $expectedHash)) {
            throw 'La lista de archivos instalados no es válida.'
        }
        $path = Join-Path $GameDirectory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-NormalizedHash $path) -ne $expectedHash) {
            throw "El archivo instalado fue modificado: $name"
        }
    }
    if (-not $names.Contains('dinput8.dll')) {
        throw 'El manifiesto no registra el proxy dinput8.dll.'
    }

    return [pscustomobject]@{
        Manifest = $manifest
        ManifestPath = $manifestPath
        OriginalHash = $expectedOriginal
        ChainedPath = $chainedPath
        BackupPath = $backupPath
        InstalledNames = @($names)
    }
}
