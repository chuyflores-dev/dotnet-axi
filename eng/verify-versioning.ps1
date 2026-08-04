[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & git -C $Repository @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw (
            "Git failed with exit code ${LASTEXITCODE}: " +
            "git $($Arguments -join ' ')`n$($output -join "`n")")
    }
}

function Add-ProbeCommit {
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [string] $Message,

        [switch] $AllowEmpty
    )

    $arguments = @(
        "-c",
        "user.name=dotnet-axi",
        "-c",
        "user.email=versioning@localhost",
        "-c",
        "commit.gpgSign=false",
        "commit",
        "--quiet",
        "--message",
        $Message
    )
    if ($AllowEmpty) {
        $arguments += "--allow-empty"
    }

    Invoke-Git -Repository $Repository -Arguments $arguments
}

function Get-HeadCommit {
    param(
        [Parameter(Mandatory)]
        [string] $Repository
    )

    $output = @(& git -C $Repository rev-parse HEAD 2>&1)
    if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) {
        throw "Unable to resolve probe commit: $($output -join "`n")"
    }

    return ([string] $output[0]).Trim()
}

function Get-ProbeVersion {
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [string] $Override
    )

    $resolver = [System.IO.Path]::Combine(
        $PSScriptRoot,
        "resolve-build-version.ps1")
    $output = & $resolver `
        -WorkingDirectory $Repository `
        -VersionOverride $Override
    $lines = @($output | ForEach-Object { [string] $_ } | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_)
    })
    if ($lines.Count -ne 1) {
        throw "Expected one resolved version; received: $($lines -join ' | ')"
    }

    return $lines[0].Trim()
}

function Assert-Version {
    param(
        [Parameter(Mandatory)]
        [string] $Actual,

        [Parameter(Mandatory)]
        [string] $Expected,

        [Parameter(Mandatory)]
        [string] $Scenario
    )

    if ($Actual -cne $Expected) {
        throw "$Scenario produced '$Actual'; expected '$Expected'."
    }
}

function Assert-VersionPattern {
    param(
        [Parameter(Mandatory)]
        [string] $Actual,

        [Parameter(Mandatory)]
        [string] $Pattern,

        [Parameter(Mandatory)]
        [string] $Scenario
    )

    if ($Actual -cnotmatch $Pattern) {
        throw "$Scenario produced '$Actual'; expected pattern '$Pattern'."
    }
}

function Assert-Refusal {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [Parameter(Mandatory)]
        [string] $Pattern,

        [Parameter(Mandatory)]
        [string] $Scenario
    )

    try {
        & $Action | Out-Null
    }
    catch {
        if ($_.Exception.Message -cnotmatch $Pattern) {
            throw "$Scenario returned an unexpected error: $($_.Exception.Message)"
        }

        return
    }

    throw "$Scenario was not refused."
}

function Assert-WorkflowContracts {
    param(
        [Parameter(Mandatory)]
        [string] $Repository
    )

    $workflow = [System.IO.File]::ReadAllText([System.IO.Path]::Combine(
        $Repository,
        ".github",
        "workflows",
        "release.yml"))
    $required = [ordered]@{
        "published-release trigger" = '(?ms)^on:\r?\n  release:\r?\n    types:\r?\n      - published\r?$'
        "read-only permissions" = '(?m)^permissions:\r?\n  contents: read\r?$'
        "release identity validation" = (
            '(?ms)^          \./eng/assert-release-candidate-inputs\.ps1 `\r?\n' +
            '            -CandidateCommit \$env:RELEASE_COMMIT `\r?\n' +
            '            -CandidateVersion \$version `\r?\n' +
            '            -ReleaseTag \$env:RELEASE_TAG `\r?\n' +
            '            -MainRef refs/remotes/origin/main\r?$')
        "candidate workflow" = '(?m)^    uses: \./\.github/workflows/release-candidate\.yml\r?$'
        "candidate dependency" = '(?ms)^  publish-nuget:\r?\n    needs:\r?\n      - validate-release\r?\n      - verify-candidate\r?$'
        "protected environment" = '(?ms)^    environment:\r?\n      name: release\r?$'
        "verified artifact" = '(?m)^          name: \$\{\{ needs\.verify-candidate\.outputs\.artifact-name \}\}\r?$'
        "missing-package refusal" = '(?m)^            if \(-not \(Test-Path -LiteralPath \$path -PathType Leaf\)\) \{\r?$'
        "publication credential" = (
            '(?ms)^      - name: Publish package and symbols to NuGet\r?\n' +
            '        shell: pwsh\r?\n        env:\r?\n' +
            '          NUGET_API_KEY: \$\{\{ secrets\.NUGET_API_KEY \}\}')
    }
    foreach ($contract in $required.GetEnumerator()) {
        if ($workflow -cnotmatch $contract.Value) {
            throw "Release workflow violates the $($contract.Key) contract."
        }
    }

    $forbidden = [ordered]@{
        "automatic non-release trigger" = '(?m)^  (push|pull_request|workflow_dispatch|schedule):'
        "write permission" = '(?m)^\s+contents: write\r?$'
        "duplicate skip" = '(?m)^\s+--skip-duplicate\s*$'
    }
    foreach ($contract in $forbidden.GetEnumerator()) {
        if ($workflow -cmatch $contract.Value) {
            throw "Release workflow contains forbidden $($contract.Key)."
        }
    }

    $credentials = [regex]::Matches(
        $workflow,
        '\$\{\{ secrets\.NUGET_API_KEY \}\}')
    if ($credentials.Count -ne 1) {
        throw "Release workflow must expose one step-scoped NuGet credential."
    }
}

$temporaryRoot = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(),
    "dnaxi-versioning-" + [System.Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

try {
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::Combine($temporaryRoot, "fixture.txt"),
        "dotnet-axi`n",
        [System.Text.UTF8Encoding]::new($false))
    Invoke-Git -Repository $temporaryRoot -Arguments @(
        "init",
        "--quiet",
        "--initial-branch=main"
    )
    Invoke-Git -Repository $temporaryRoot -Arguments @("add", "fixture.txt")
    Add-ProbeCommit -Repository $temporaryRoot -Message "initial"
    $firstCommit = Get-HeadCommit -Repository $temporaryRoot
    Add-ProbeCommit `
        -Repository $temporaryRoot `
        -Message "untagged" `
        -AllowEmpty
    $candidateCommit = Get-HeadCommit -Repository $temporaryRoot

    Assert-VersionPattern `
        -Actual (Get-ProbeVersion -Repository $temporaryRoot) `
        -Pattern '^0\.2\.0-alpha\.0\.[0-9]+$' `
        -Scenario "No-tag build"
    Assert-Version `
        -Actual (Get-ProbeVersion `
            -Repository $temporaryRoot `
            -Override "0.2.0") `
        -Expected "0.2.0" `
        -Scenario "Candidate override"

    Invoke-Git `
        -Repository $temporaryRoot `
        -Arguments @("-c", "tag.gpgSign=false", "tag", "v0.2.0")
    Assert-Version `
        -Actual (Get-ProbeVersion -Repository $temporaryRoot) `
        -Expected "0.2.0" `
        -Scenario "Stable tag"

    $validator = [System.IO.Path]::Combine(
        $PSScriptRoot,
        "assert-release-candidate-inputs.ps1")
    & $validator `
        -RepositoryRoot $temporaryRoot `
        -CandidateCommit $candidateCommit `
        -CandidateVersion "0.2.0" `
        -ReleaseTag "v0.2.0" `
        -MainRef refs/heads/main
    Assert-Refusal `
        -Scenario "Malformed release tag" `
        -Pattern "not a valid SemVer" `
        -Action {
            & $validator `
                -RepositoryRoot $temporaryRoot `
                -CandidateCommit $candidateCommit `
                -CandidateVersion "not-semver" `
                -ReleaseTag "vnot-semver" `
                -MainRef refs/heads/main
        }
    Assert-Refusal `
        -Scenario "Mismatched release tag" `
        -Pattern "does not match" `
        -Action {
            & $validator `
                -RepositoryRoot $temporaryRoot `
                -CandidateCommit $candidateCommit `
                -CandidateVersion "0.2.0" `
                -ReleaseTag "v0.2.1" `
                -MainRef refs/heads/main
        }
    Invoke-Git -Repository $temporaryRoot -Arguments @(
        "-c",
        "tag.gpgSign=false",
        "tag",
        "v0.2.1",
        $firstCommit
    )
    Assert-Refusal `
        -Scenario "Disagreeing tag commit" `
        -Pattern "does not identify" `
        -Action {
            & $validator `
                -RepositoryRoot $temporaryRoot `
                -CandidateCommit $candidateCommit `
                -CandidateVersion "0.2.1" `
                -ReleaseTag "v0.2.1" `
                -MainRef refs/heads/main
        }

    Add-ProbeCommit `
        -Repository $temporaryRoot `
        -Message "next" `
        -AllowEmpty
    Assert-Version `
        -Actual (Get-ProbeVersion -Repository $temporaryRoot) `
        -Expected "0.3.0-alpha.0.1" `
        -Scenario "Post-release build"

    Invoke-Git `
        -Repository $temporaryRoot `
        -Arguments @(
            "-c",
            "tag.gpgSign=false",
            "tag",
            "v0.3.0-rc.1"
        )
    Assert-Version `
        -Actual (Get-ProbeVersion -Repository $temporaryRoot) `
        -Expected "0.3.0-rc.1" `
        -Scenario "Prerelease tag"

    Invoke-Git -Repository $temporaryRoot -Arguments @(
        "switch",
        "--quiet",
        "--create",
        "feature",
        $candidateCommit
    )
    Add-ProbeCommit `
        -Repository $temporaryRoot `
        -Message "outside-main" `
        -AllowEmpty
    $outsideMain = Get-HeadCommit -Repository $temporaryRoot
    Invoke-Git -Repository $temporaryRoot -Arguments @(
        "-c",
        "tag.gpgSign=false",
        "tag",
        "v0.4.0",
        $outsideMain
    )
    Assert-Refusal `
        -Scenario "Commit outside main" `
        -Pattern "not reachable" `
        -Action {
            & $validator `
                -RepositoryRoot $temporaryRoot `
                -CandidateCommit $outsideMain `
                -CandidateVersion "0.4.0" `
                -ReleaseTag "v0.4.0" `
                -MainRef refs/heads/main
        }
    Assert-Refusal `
        -Scenario "NuGet build metadata" `
        -Pattern "build metadata" `
        -Action {
            & $validator `
                -RepositoryRoot $temporaryRoot `
                -CandidateCommit $outsideMain `
                -CandidateVersion "0.4.0+build" `
                -ReleaseTag "v0.4.0+build" `
                -MainRef refs/heads/main
        }

    Assert-WorkflowContracts -Repository ([System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine($PSScriptRoot, "..")))
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host (
    "Verified tag-derived versions and protected publication controls " +
    "without creating release state.")
