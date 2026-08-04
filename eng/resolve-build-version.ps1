[CmdletBinding()]
param(
    [string] $WorkingDirectory,

    [string] $VersionOverride
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    $WorkingDirectory = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine($PSScriptRoot, ".."))
}
else {
    $WorkingDirectory = (
        Resolve-Path -LiteralPath $WorkingDirectory
    ).Path
}

$previousOverride = [System.Environment]::GetEnvironmentVariable(
    "MINVERVERSIONOVERRIDE",
    [System.EnvironmentVariableTarget]::Process)
$toolRoot = [System.IO.Path]::GetFullPath(
    [System.IO.Path]::Combine($PSScriptRoot, ".."))
Push-Location -LiteralPath $toolRoot
try {
    [System.Environment]::SetEnvironmentVariable(
        "MINVERVERSIONOVERRIDE",
        $VersionOverride,
        [System.EnvironmentVariableTarget]::Process)
    $output = & dotnet minver `
        $WorkingDirectory `
        --auto-increment minor `
        --minimum-major-minor 0.2 `
        --tag-prefix v `
        --verbosity error 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw (
            "MinVer failed with exit code ${LASTEXITCODE}: " +
            ($output -join "`n"))
    }
}
finally {
    Pop-Location
    [System.Environment]::SetEnvironmentVariable(
        "MINVERVERSIONOVERRIDE",
        $previousOverride,
        [System.EnvironmentVariableTarget]::Process)
}

$lines = @($output | ForEach-Object { [string] $_ } | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_)
})
if ($lines.Count -ne 1) {
    throw "MinVer returned an unexpected result: $($lines -join ' | ')"
}

$version = $lines[0].Trim()
if ($version -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "MinVer returned invalid SemVer '$version'."
}

Write-Output $version
