[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $CandidateCommit,

    [Parameter(Mandatory)]
    [string] $CandidateVersion,

    [string] $RepositoryRoot,

    [string] $RemoteName = "origin",

    [string] $MainRef
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

if ($RemoteName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
    throw "Remote name '$RemoteName' is not valid for release-tag validation."
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [System.IO.Path]::Combine($PSScriptRoot, "..")
}
$script:ResolvedRepositoryRoot = (
    Resolve-Path -LiteralPath $RepositoryRoot
).Path

if ([string]::IsNullOrWhiteSpace($MainRef)) {
    $MainRef = "refs/remotes/$RemoteName/main"
}
if ($MainRef -cnotmatch '^refs/[A-Za-z0-9._/-]+$') {
    throw "Main ref '$MainRef' is not a valid full Git ref."
}

$objectType = Invoke-Git -Arguments @("cat-file", "-t", $CandidateCommit)
if ($objectType.ExitCode -ne 0 -or
    $objectType.Lines.Count -ne 1 -or
    $objectType.Lines[0] -cne "commit") {
    throw "Candidate '$CandidateCommit' does not identify a local Git commit."
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
    throw "Candidate commit '$CandidateCommit' is not reachable from '$MainRef'."
}
if ($membership.ExitCode -ne 0) {
    throw (
        "Unable to validate candidate membership in '$MainRef': " +
        ($membership.Lines -join "`n"))
}

$releaseTag = "v$CandidateVersion"
$releaseRef = "refs/tags/$releaseTag"
$peeledReleaseRef = "$releaseRef^{}"
$remoteTags = Invoke-Git -Arguments @(
    "ls-remote",
    "--tags",
    $RemoteName,
    $releaseRef,
    $peeledReleaseRef,
    "$releaseRef/*"
)
if ($remoteTags.ExitCode -ne 0) {
    throw (
        "Unable to inspect release tags on remote '$RemoteName': " +
        ($remoteTags.Lines -join "`n"))
}

$remoteRefs = [System.Collections.Generic.Dictionary[string, string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($line in $remoteTags.Lines) {
    if ($line -cnotmatch '^([0-9a-f]{40})\s+(\S+)$') {
        throw "Remote '$RemoteName' returned an unexpected tag record: $line"
    }

    $sha = $Matches[1]
    $ref = $Matches[2]
    if (-not $remoteRefs.TryAdd($ref, $sha)) {
        throw "Remote '$RemoteName' returned duplicate tag ref '$ref'."
    }
}

if ($remoteRefs.ContainsKey($releaseRef)) {
    $tagTarget = $remoteRefs[$releaseRef]
    if ($remoteRefs.ContainsKey($peeledReleaseRef)) {
        $tagTarget = $remoteRefs[$peeledReleaseRef]
    }

    if ($tagTarget -ceq $CandidateCommit) {
        throw (
            "Release tag '$releaseTag' already exists for candidate commit " +
            "'$CandidateCommit'.")
    }

    throw (
        "Release tag '$releaseTag' points to '$tagTarget' and conflicts with " +
        "candidate commit '$CandidateCommit'.")
}

$namespaceConflict = @(
    $remoteRefs.Keys |
        Where-Object { $_.StartsWith("$releaseRef/", [System.StringComparison]::Ordinal) }
)
if ($namespaceConflict.Count -ne 0) {
    throw (
        "Release tag '$releaseTag' conflicts with existing tag namespace " +
        "'$($namespaceConflict[0])'.")
}

Write-Host (
    "Validated release tag '$releaseTag' for main commit " +
    "'$CandidateCommit'; no ref was created.")
Write-Output $releaseTag
