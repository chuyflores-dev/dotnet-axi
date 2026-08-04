[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [Parameter(Mandatory)]
    [string] $PlatformEvidenceDirectory,

    [Parameter(Mandatory)]
    [string] $ExpectedCommit,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [Parameter(Mandatory)]
    [string] $OutputPath
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
$resolvedPlatformEvidenceDirectory = (
    Resolve-Path -LiteralPath $PlatformEvidenceDirectory
).Path
$packageEvidencePath = [System.IO.Path]::Combine(
    $resolvedPackageDirectory,
    "package-evidence.json")
$checksumPath = [System.IO.Path]::Combine(
    $resolvedPackageDirectory,
    "checksums.sha256")
foreach ($requiredPath in @($packageEvidencePath, $checksumPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required candidate evidence '$requiredPath' is missing."
    }
}

$packageEvidence = Get-Content `
    -LiteralPath $packageEvidencePath `
    -Raw | ConvertFrom-Json
if ($packageEvidence.schema -cne
    "dotnet-axi/release-candidate-package-evidence/v1") {
    throw "Package evidence has an unsupported schema."
}
if ($packageEvidence.candidate_commit -cne $ExpectedCommit -or
    $packageEvidence.observed_repository_commit -cne $ExpectedCommit) {
    throw "Package evidence commit disagrees with '$ExpectedCommit'."
}
if ($packageEvidence.requested_version -cne $ExpectedVersion -or
    $packageEvidence.observed_version -cne $ExpectedVersion) {
    throw "Package evidence version disagrees with '$ExpectedVersion'."
}
foreach ($field in @("sdk_version", "os", "rid")) {
    if ([string]::IsNullOrWhiteSpace([string] $packageEvidence.$field)) {
        throw "Package evidence field '$field' is missing."
    }
}

$packageFiles = @($packageEvidence.package_files)
if ($packageFiles.Count -ne 2) {
    throw "Package evidence must identify exactly two package files."
}
$actualPackageFiles = @(
    Get-ChildItem -LiteralPath $resolvedPackageDirectory -File |
        Where-Object {
            $_.Name -like "dotnet-axi.*.nupkg" -or
            $_.Name -like "dotnet-axi.*.snupkg"
        }
)
if ($actualPackageFiles.Count -ne 2) {
    throw "Candidate bundle must contain one package and one symbol package."
}

$expectedChecksumLines = [System.Collections.Generic.List[string]]::new()
foreach ($fileEvidence in $packageFiles) {
    $name = [string] $fileEvidence.name
    if ([System.IO.Path]::GetFileName($name) -cne $name) {
        throw "Package evidence contains unsafe file name '$name'."
    }
    $filePath = [System.IO.Path]::Combine($resolvedPackageDirectory, $name)
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "Candidate package file '$name' is missing."
    }
    $actualHash = (Get-FileHash `
        -LiteralPath $filePath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    if ([string] $fileEvidence.sha256 -cne $actualHash) {
        throw "Candidate package checksum disagrees for '$name'."
    }
    $expectedChecksumLines.Add("$actualHash  $name")
}
$expectedChecksumText = (($expectedChecksumLines -join "`n") + "`n")
$actualChecksumText = [System.IO.File]::ReadAllText($checksumPath)
if ($actualChecksumText -cne $expectedChecksumText) {
    throw "checksums.sha256 disagrees with the candidate package files."
}

$platformFiles = @(
    Get-ChildItem `
        -LiteralPath $resolvedPlatformEvidenceDirectory `
        -Filter "*.json" `
        -File
)
if ($platformFiles.Count -ne 3) {
    throw "Expected three platform evidence documents; found $($platformFiles.Count)."
}
$platforms = @(
    foreach ($platformFile in $platformFiles) {
        $platform = Get-Content `
            -LiteralPath $platformFile.FullName `
            -Raw | ConvertFrom-Json
        if ($platform.schema -cne
            "dotnet-axi/release-candidate-platform-evidence/v1") {
            throw "Platform evidence '$($platformFile.Name)' has an unsupported schema."
        }
        if ($platform.candidate_commit -cne $ExpectedCommit) {
            throw "Platform evidence '$($platformFile.Name)' has the wrong commit."
        }
        if ($platform.requested_version -cne $ExpectedVersion -or
            $platform.observed_package_version -cne $ExpectedVersion) {
            throw "Platform evidence '$($platformFile.Name)' has the wrong version."
        }
        foreach ($invocation in @("global", "local", "dnx")) {
            if ([string] $platform.observed_versions.$invocation -cne
                $ExpectedVersion) {
                throw (
                    "Platform evidence '$($platformFile.Name)' reports " +
                    "the wrong $invocation version.")
            }
        }
        foreach ($field in @("runner_os", "sdk_version", "os", "rid")) {
            if ([string]::IsNullOrWhiteSpace([string] $platform.$field)) {
                throw (
                    "Platform evidence '$($platformFile.Name)' is missing " +
                    "'$field'.")
            }
        }

        $platformPackageFiles = @($platform.package_files)
        if ($platformPackageFiles.Count -ne $packageFiles.Count) {
            throw "Platform evidence '$($platformFile.Name)' has incomplete checksums."
        }
        foreach ($fileEvidence in $packageFiles) {
            $match = @(
                $platformPackageFiles |
                    Where-Object { $_.name -ceq $fileEvidence.name }
            )
            if ($match.Count -ne 1 -or
                [string] $match[0].sha256 -cne [string] $fileEvidence.sha256) {
                throw (
                    "Platform evidence '$($platformFile.Name)' checksum " +
                    "disagrees for '$($fileEvidence.name)'.")
            }
        }

        $platform
    }
)
$runnerOperatingSystems = @(
    $platforms |
        ForEach-Object { [string] $_.runner_os } |
        Sort-Object -Unique
)
if ($runnerOperatingSystems.Count -ne 3 -or
    $runnerOperatingSystems -cnotcontains "Linux" -or
    $runnerOperatingSystems -cnotcontains "macOS" -or
    $runnerOperatingSystems -cnotcontains "Windows") {
    throw (
        "Platform evidence must cover Linux, macOS, and Windows; found " +
        "'$($runnerOperatingSystems -join ',')'.")
}

$aggregate = [ordered]@{
    schema = "dotnet-axi/release-candidate-evidence/v1"
    candidate_commit = $ExpectedCommit
    observed_repository_commit = [string] $packageEvidence.observed_repository_commit
    requested_version = $ExpectedVersion
    observed_version = [string] $packageEvidence.observed_version
    package_producer = [ordered]@{
        sdk_version = [string] $packageEvidence.sdk_version
        os = [string] $packageEvidence.os
        rid = [string] $packageEvidence.rid
    }
    package_files = $packageFiles
    platform_verification = @(
        $platforms | Sort-Object -Property runner_os
    )
}
$outputDirectory = [System.IO.Path]::GetDirectoryName(
    [System.IO.Path]::GetFullPath($OutputPath))
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($OutputPath),
    (($aggregate | ConvertTo-Json -Depth 8) + "`n"),
    [System.Text.UTF8Encoding]::new($false))

Write-Host (
    "Completed release-candidate evidence for $ExpectedCommit at " +
    "version $ExpectedVersion.")
