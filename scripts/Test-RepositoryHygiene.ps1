[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$safeRoot = $root.Replace('\', '/')

$repositoryFiles = @(
    git -c "safe.directory=$safeRoot" -C $root ls-files `
        --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) {
    throw 'No se pudo inventariar el repositorio.'
}

$forbiddenFiles = @($repositoryFiles | Where-Object {
    $_ -match '(^|/)(_analysis_v|artifacts|bin|obj)(/|$)' -or
    $_ -match '\.(dll|exe|pdb|dmp|zip|log)$'
})
if ($forbiddenFiles.Count -gt 0) {
    throw (
        'Hay binarios o artefactos versionados: ' +
        ($forbiddenFiles -join ', '))
}

$history = git -c "safe.directory=$safeRoot" -C $root log `
    --all -p --no-ext-diff
if ($LASTEXITCODE -ne 0) {
    throw 'No se pudo revisar el historial.'
}
$secretPatterns = @(
    'AIza[0-9A-Za-z_-]{35}',
    'github_pat_[0-9A-Za-z_]{20,}',
    'gh[pousr]_[0-9A-Za-z]{20,}',
    '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
)
foreach ($pattern in $secretPatterns) {
    if ($history -match $pattern) {
        throw "El historial coincide con un patrón de secreto: $pattern"
    }
    foreach ($relativePath in $repositoryFiles | Where-Object {
            $_ -match '\.(cs|cpp|h|ps1|cmd|json|ya?ml|md|txt|props|rc|def|gitignore)$'
        }) {
        $content = Get-Content -LiteralPath (
            Join-Path $root $relativePath) -Raw
        if ($content -match $pattern) {
            throw (
                "El archivo $relativePath coincide con un patrón " +
                "de secreto: $pattern")
        }
    }
}

Write-Host 'Higiene del repositorio e historial: OK'
