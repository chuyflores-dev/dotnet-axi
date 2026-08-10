[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [string] $PackageRoot = "artifacts/packages/local"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Version) -or
    $Version -in @(".", "..") -or
    $Version.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
    $Version.IndexOfAny([char[]] @('/', '\')) -ge 0) {
    throw "Version must be a non-empty NuGet version without path separators."
}

$repositoryRoot = [System.IO.Path]::GetFullPath(
    [System.IO.Path]::Combine($PSScriptRoot, ".."))
$resolvedPackageRoot = [System.IO.Path]::GetFullPath(
    $PackageRoot,
    (Get-Location).Path)
[System.IO.Directory]::CreateDirectory($resolvedPackageRoot) | Out-Null

$lockPath = [System.IO.Path]::Combine(
    $resolvedPackageRoot,
    ".dnaxi-pack.lock")
$lockStream = $null
$stagingDirectory = $null
try {
    try {
        $lockStream = [System.IO.File]::Open(
            $lockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    }
    catch [System.IO.IOException] {
        throw (
            "Another local dnaxi candidate pack is already running for " +
            "'$resolvedPackageRoot'.")
    }

    $versionDirectory = [System.IO.Path]::Combine(
        $resolvedPackageRoot,
        $Version)
    if ((Test-Path -LiteralPath $versionDirectory)) {
        throw (
            "Candidate package version '$Version' already exists; choose a " +
            "new candidate version so dnx cannot reuse stale package state.")
    }

    $stagingName = (
        ".dnaxi-pack-" + [System.Guid]::NewGuid().ToString("N") + ".tmp")
    $stagingDirectory = [System.IO.Path]::Combine(
        $resolvedPackageRoot,
        $stagingName)
    [System.IO.Directory]::CreateDirectory($stagingDirectory) | Out-Null
    $projectPath = [System.IO.Path]::Combine(
        $repositoryRoot,
        "src",
        "DotNetAxi.Cli",
        "DotNetAxi.Cli.csproj")
    $dotnetHost = if ([string]::IsNullOrWhiteSpace(
            $env:DOTNET_HOST_PATH)) {
        "dotnet"
    }
    else {
        $env:DOTNET_HOST_PATH
    }
    & $dotnetHost pack $projectPath `
        --configuration Release `
        --no-restore `
        --output $stagingDirectory `
        "-p:DotNetAxiBuildVersion=$Version"
    if (-not $?) {
        throw "dotnet pack failed."
    }

    $packages = @(Get-ChildItem -LiteralPath $stagingDirectory -File)
    $toolPackages = @($packages | Where-Object {
        $_.Extension -ceq ".nupkg"
    })
    $symbolPackages = @($packages | Where-Object {
        $_.Extension -ceq ".snupkg"
    })
    if ($toolPackages.Count -ne 1 -or $symbolPackages.Count -ne 1) {
        throw (
            "Expected exactly one .nupkg and one .snupkg in " +
            "the candidate staging directory.")
    }

    [System.IO.Directory]::Move($stagingDirectory, $versionDirectory)
    $stagingDirectory = $null
    Write-Output $versionDirectory
}
finally {
    if ($null -ne $stagingDirectory -and
        [System.IO.Directory]::Exists($stagingDirectory)) {
        [System.IO.Directory]::Delete($stagingDirectory, $true)
    }
    if ($null -ne $lockStream) {
        $lockStream.Dispose()
    }
}
