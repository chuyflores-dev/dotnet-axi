[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $CandidateCommit,

    [Parameter(Mandatory)]
    [string] $CandidateVersion,

    [string] $RepositoryRoot,

    [string] $ReleaseTag,

    [string] $MainRef,

    [switch] $RequireClean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = @(& git -C $script:ResolvedRepositoryRoot @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $lines = @(
        $output |
            ForEach-Object { [string] $_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )

    return [pscustomobject]@{
        ExitCode = $exitCode
        Lines = [string[]] $lines
    }
}

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
$script:ResolvedRepositoryRoot = (
    Resolve-Path -LiteralPath $RepositoryRoot
).Path

$head = Invoke-Git -Arguments @("rev-parse", "--verify", "HEAD^{commit}")
if ($head.ExitCode -ne 0 -or
    $head.Lines.Count -ne 1 -or
    $head.Lines[0] -cne $CandidateCommit) {
    throw (
        "Checked-out commit '$($head.Lines -join ' | ')' does not match " +
        "requested candidate commit '$CandidateCommit'.")
}

$objectType = Invoke-Git -Arguments @("cat-file", "-t", $CandidateCommit)
if ($objectType.ExitCode -ne 0 -or
    $objectType.Lines.Count -ne 1 -or
    $objectType.Lines[0] -cne "commit") {
    throw "Candidate '$CandidateCommit' does not identify a Git commit."
}

$hasReleaseTag = -not [string]::IsNullOrWhiteSpace($ReleaseTag)
$hasMainRef = -not [string]::IsNullOrWhiteSpace($MainRef)
if ($hasReleaseTag -ne $hasMainRef) {
    throw "Release tag and main ref must be validated together."
}

if ($hasReleaseTag) {
    if ($CandidateVersion.Contains("+")) {
        throw "NuGet release versions cannot contain build metadata."
    }

    $expectedTag = "v$CandidateVersion"
    if ($ReleaseTag -cne $expectedTag) {
        throw "Release tag '$ReleaseTag' does not match '$expectedTag'."
    }

    $tagCommit = Invoke-Git -Arguments @(
        "rev-parse",
        "--verify",
        "refs/tags/$ReleaseTag^{commit}"
    )
    if ($tagCommit.ExitCode -ne 0 -or $tagCommit.Lines.Count -ne 1) {
        throw "Unable to resolve release tag '$ReleaseTag'."
    }
    if ($tagCommit.Lines[0] -cne $CandidateCommit) {
        throw "Release tag '$ReleaseTag' does not identify the candidate commit."
    }

    $mainCommit = Invoke-Git -Arguments @(
        "rev-parse",
        "--verify",
        "$MainRef^{commit}"
    )
    if ($mainCommit.ExitCode -ne 0 -or $mainCommit.Lines.Count -ne 1) {
        throw "Unable to resolve main ref '$MainRef'."
    }

    $membership = Invoke-Git -Arguments @(
        "merge-base",
        "--is-ancestor",
        $CandidateCommit,
        $MainRef
    )
    if ($membership.ExitCode -eq 1) {
        throw "Candidate commit is not reachable from '$MainRef'."
    }
    if ($membership.ExitCode -ne 0) {
        throw "Unable to validate candidate membership in '$MainRef'."
    }
}

if ($RequireClean) {
    $status = Invoke-Git -Arguments @(
        "status",
        "--porcelain=v1",
        "--untracked-files=all"
    )
    if ($status.ExitCode -ne 0) {
        throw "Unable to inspect repository status."
    }
    if ($status.Lines.Count -ne 0) {
        throw (
            "Candidate verification changed repository state:`n" +
            ($status.Lines -join "`n"))
    }
}

Write-Host (
    "Verified release-candidate inputs: commit $CandidateCommit, " +
    "version $CandidateVersion.")
