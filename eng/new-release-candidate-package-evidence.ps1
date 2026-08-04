[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [Parameter(Mandatory)]
    [string] $ExpectedCommit,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [string] $EvidencePath,

    [string] $ChecksumPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

& ([System.IO.Path]::Combine(
        $PSScriptRoot,
        "assert-release-candidate-inputs.ps1")) `
    -CandidateCommit $ExpectedCommit `
    -CandidateVersion $ExpectedVersion

$resolvedPackageDirectory = (
    Resolve-Path -LiteralPath $PackageDirectory
).Path
if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    $EvidencePath = [System.IO.Path]::Combine(
        $resolvedPackageDirectory,
        "package-evidence.json")
}
if ([string]::IsNullOrWhiteSpace($ChecksumPath)) {
    $ChecksumPath = [System.IO.Path]::Combine(
        $resolvedPackageDirectory,
        "checksums.sha256")
}

$candidatePackageFiles = @(
    Get-ChildItem -LiteralPath $resolvedPackageDirectory -File |
        Where-Object { $_.Extension -iin @(".nupkg", ".snupkg") }
)
if ($candidatePackageFiles.Count -ne 2) {
    throw (
        "Expected exactly one package and one symbol package in " +
        "'$resolvedPackageDirectory'; found " +
        "$($candidatePackageFiles.Count) package files.")
}
$expectedPackagePath = [System.IO.Path]::Combine(
    $resolvedPackageDirectory,
    "dotnet-axi.$ExpectedVersion.nupkg")
$expectedSymbolPackagePath = [System.IO.Path]::Combine(
    $resolvedPackageDirectory,
    "dotnet-axi.$ExpectedVersion.snupkg")
foreach ($expectedPath in @(
        $expectedPackagePath,
        $expectedSymbolPackagePath)) {
    if (-not (Test-Path -LiteralPath $expectedPath -PathType Leaf)) {
        throw "Expected candidate package '$expectedPath' is missing."
    }
}
$package = Get-Item -LiteralPath $expectedPackagePath
$symbolPackagePath = $expectedSymbolPackagePath

$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $nuspecEntries = @(
        $archive.Entries |
            Where-Object { $_.FullName -like "*.nuspec" }
    )
    if ($nuspecEntries.Count -ne 1) {
        throw "Expected one nuspec entry; found $($nuspecEntries.Count)."
    }

    $stream = $nuspecEntries[0].Open()
    $reader = [System.IO.StreamReader]::new(
        $stream,
        [System.Text.UTF8Encoding]::new($false, $true),
        $true)
    try {
        [xml] $nuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }

    $metadata = $nuspec.package.metadata
    $observedVersion = [string] $metadata.version
    $observedCommit = [string] $metadata.repository.commit
    if ([string] $metadata.id -cne "dotnet-axi") {
        throw "Package ID is '$($metadata.id)', expected 'dotnet-axi'."
    }
    if ($observedVersion -cne $ExpectedVersion) {
        throw (
            "Package version is '$observedVersion', expected " +
            "'$ExpectedVersion'.")
    }
    if ($observedCommit -cne $ExpectedCommit) {
        throw (
            "Package repository commit is '$observedCommit', expected " +
            "'$ExpectedCommit'.")
    }
}
finally {
    $archive.Dispose()
}

$dotnetOutput = & dotnet --version 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "dotnet --version failed: $($dotnetOutput -join "`n")"
}
$sdkVersions = @(
    $dotnetOutput |
        ForEach-Object { [string] $_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
if ($sdkVersions.Count -ne 1) {
    throw "dotnet --version returned an unexpected result."
}

$artifactPaths = @($package.FullName, $symbolPackagePath)
$artifactNames = @($artifactPaths | ForEach-Object {
    [System.IO.Path]::GetFileName($_)
})
[System.Array]::Sort(
    $artifactNames,
    [System.StringComparer]::Ordinal)
$packageFiles = @(
    foreach ($name in $artifactNames) {
        $path = [System.IO.Path]::Combine($resolvedPackageDirectory, $name)
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        [ordered]@{
            name = $name
            sha256 = $hash
        }
    }
)

$checksumLines = @(
    $packageFiles | ForEach-Object { "$($_.sha256)  $($_.name)" }
)
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($ChecksumPath),
    (($checksumLines -join "`n") + "`n"),
    [System.Text.UTF8Encoding]::new($false))

$evidence = [ordered]@{
    schema = "dotnet-axi/release-candidate-package-evidence/v1"
    candidate_commit = $ExpectedCommit
    requested_version = $ExpectedVersion
    observed_version = $observedVersion
    observed_repository_commit = $observedCommit
    sdk_version = $sdkVersions[0].Trim()
    os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
    package_files = $packageFiles
}
$json = $evidence | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($EvidencePath),
    ($json + "`n"),
    [System.Text.UTF8Encoding]::new($false))

Write-Host (
    "Recorded package evidence for dotnet-axi $ExpectedVersion at " +
    "commit $ExpectedCommit.")
