[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $CandidateCommit,

    [Parameter(Mandatory)]
    [string] $CandidateVersion,

    [string] $RepositoryRoot,

    [switch] $RequireClean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($CandidateCommit -cnotmatch '^[0-9a-f]{40}$') {
    throw "Candidate commit must be an exact 40-character lowercase Git commit SHA."
}

$semVerPattern = (
    '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)' +
    '(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)' +
    '(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?' +
    '(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')
if ($CandidateVersion.Length -gt 128 -or
    $CandidateVersion -cnotmatch $semVerPattern) {
    throw "Candidate version '$CandidateVersion' is not a valid SemVer 2.0 version."
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [System.IO.Path]::Combine($PSScriptRoot, "..")
}
$resolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

$headOutput = & git -C $resolvedRepositoryRoot rev-parse --verify HEAD 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Unable to resolve repository HEAD: $($headOutput -join "`n")"
}
$headLines = @(
    $headOutput |
        ForEach-Object { [string] $_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
if ($headLines.Count -ne 1 -or $headLines[0].Trim() -cne $CandidateCommit) {
    throw (
        "Checked-out commit '$($headLines -join ' | ')' does not match " +
        "requested candidate commit '$CandidateCommit'.")
}

$objectType = & git -C $resolvedRepositoryRoot cat-file -t $CandidateCommit 2>&1
if ($LASTEXITCODE -ne 0 -or ($objectType -join "").Trim() -cne "commit") {
    throw "Candidate '$CandidateCommit' does not identify a Git commit."
}

if ($RequireClean) {
    $status = & git -C $resolvedRepositoryRoot status `
        --porcelain=v1 `
        --untracked-files=all 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect repository status: $($status -join "`n")"
    }
    $statusLines = @(
        $status |
            ForEach-Object { [string] $_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($statusLines.Count -ne 0) {
        throw (
            "Candidate verification changed repository state:`n" +
            ($statusLines -join "`n"))
    }
}

Write-Host (
    "Verified release-candidate inputs: commit $CandidateCommit, " +
    "version $CandidateVersion.")
