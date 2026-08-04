[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Equal {
    param(
        [AllowNull()]
        [object] $Actual,

        [AllowNull()]
        [object] $Expected,

        [Parameter(Mandatory)]
        [string] $Scenario
    )

    if ($Actual -cne $Expected) {
        throw "$Scenario produced '$Actual'; expected '$Expected'."
    }
}

function Assert-Matches {
    param(
        [Parameter(Mandatory)]
        [string] $Text,

        [Parameter(Mandatory)]
        [string] $Pattern,

        [Parameter(Mandatory)]
        [string] $Scenario
    )

    if ($Text -cnotmatch $Pattern) {
        throw "$Scenario does not match required pattern '$Pattern'."
    }
}

function Assert-NotMatches {
    param(
        [Parameter(Mandatory)]
        [string] $Text,

        [Parameter(Mandatory)]
        [string] $Pattern,

        [Parameter(Mandatory)]
        [string] $Scenario
    )

    if ($Text -cmatch $Pattern) {
        throw "$Scenario unexpectedly matches forbidden pattern '$Pattern'."
    }
}

function Get-WorkflowJob {
    param(
        [Parameter(Mandatory)]
        [string] $Workflow,

        [Parameter(Mandatory)]
        [string] $JobName
    )

    $escapedName = [System.Text.RegularExpressions.Regex]::Escape($JobName)
    $match = [regex]::Match(
        $Workflow,
        "(?ms)^  ${escapedName}:\r?\n(?<body>.*?)(?=^  [A-Za-z0-9_-]+:\r?\n|\z)")
    if (-not $match.Success) {
        throw "Workflow job '$JobName' was not found."
    }

    return $match.Value
}

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [switch] $AllowFailure
    )

    $output = @(& git -C $Repository @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $lines = @(
        $output |
            ForEach-Object { [string] $_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw (
            "Git failed with exit code ${exitCode}: " +
            "git $($Arguments -join ' ')`n$($lines -join "`n")")
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Lines = [string[]] $lines
    }
}

function Add-FixtureCommit {
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [string] $Content,

        [Parameter(Mandatory)]
        [string] $Message
    )

    [System.IO.File]::WriteAllText(
        [System.IO.Path]::Combine($Repository, "fixture.txt"),
        "$Content`n",
        [System.Text.UTF8Encoding]::new($false))
    Invoke-Git -Repository $Repository -Arguments @("add", "fixture.txt") |
        Out-Null
    Invoke-Git -Repository $Repository -Arguments @(
        "-c",
        "user.name=dotnet-axi",
        "-c",
        "user.email=release-tag@localhost",
        "-c",
        "commit.gpgSign=false",
        "commit",
        "--quiet",
        "--message",
        $Message
    ) | Out-Null

    $commit = Invoke-Git -Repository $Repository -Arguments @(
        "rev-parse",
        "HEAD"
    )
    return $commit.Lines[0]
}

function Assert-Refusal {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [Parameter(Mandatory)]
        [string] $MessagePattern,

        [Parameter(Mandatory)]
        [string] $Scenario
    )

    try {
        $null = & $Action
    }
    catch {
        $message = $_.Exception.Message
        if ($message -cnotmatch $MessagePattern) {
            throw (
                "$Scenario was refused with unexpected message '$message'; " +
                "expected pattern '$MessagePattern'.")
        }

        return
    }

    throw "$Scenario was not refused."
}

$repositoryRoot = [System.IO.Path]::GetFullPath(
    [System.IO.Path]::Combine($PSScriptRoot, ".."))
$workflowPath = [System.IO.Path]::Combine(
    $repositoryRoot,
    ".github",
    "workflows",
    "release-tag.yml")
$candidateWorkflowPath = [System.IO.Path]::Combine(
    $repositoryRoot,
    ".github",
    "workflows",
    "release-candidate.yml")
$validatorPath = [System.IO.Path]::Combine(
    $repositoryRoot,
    "eng",
    "assert-release-tag.ps1")

$workflow = [System.IO.File]::ReadAllText($workflowPath)
$candidateWorkflow = [System.IO.File]::ReadAllText($candidateWorkflowPath)

Assert-Matches `
    -Text $workflow `
    -Pattern '(?m)^on:\r?\n  workflow_dispatch:\r?$' `
    -Scenario "Release-tag trigger"
Assert-NotMatches `
    -Text $workflow `
    -Pattern '(?m)^  (push|pull_request|workflow_call|schedule):' `
    -Scenario "Release-tag trigger"
foreach ($inputName in @("commit", "version", "dry_run")) {
    Assert-Matches `
        -Text $workflow `
        -Pattern "(?m)^      ${inputName}:\r?$" `
        -Scenario "Release-tag input '$inputName'"
}
Assert-Matches `
    -Text $workflow `
    -Pattern '(?ms)^      commit:\r?\n        description: .*\r?\n        required: true\r?\n        type: string\r?$' `
    -Scenario "Exact commit input contract"
Assert-Matches `
    -Text $workflow `
    -Pattern '(?ms)^      version:\r?\n        description: .*\r?\n        required: true\r?\n        type: string\r?$' `
    -Scenario "Exact version input contract"
Assert-Matches `
    -Text $workflow `
    -Pattern '(?ms)^      dry_run:\r?\n        description: .*\r?\n        required: true\r?\n        default: true\r?\n        type: boolean\r?$' `
    -Scenario "Safe dry-run default"
Assert-Matches `
    -Text $workflow `
    -Pattern '(?m)^permissions: \{\}\r?$' `
    -Scenario "Default workflow permissions"
Assert-Matches `
    -Text $workflow `
    -Pattern '(?ms)^concurrency:\r?\n  group: release-tag-v\$\{\{ inputs\.version \}\}\r?\n  cancel-in-progress: false\r?$' `
    -Scenario "Release-tag concurrency"

$writePermissionCount = [regex]::Matches(
    $workflow,
    '(?m)^\s+contents: write\r?$').Count
Assert-Equal `
    -Actual $writePermissionCount `
    -Expected 1 `
    -Scenario "Write-enabled job count"

$validateJob = Get-WorkflowJob -Workflow $workflow -JobName "validate-tag"
$candidateJob = Get-WorkflowJob -Workflow $workflow -JobName "verify-candidate"
$dryRunJob = Get-WorkflowJob -Workflow $workflow -JobName "report-dry-run"
$createJob = Get-WorkflowJob -Workflow $workflow -JobName "create-tag"

Assert-Matches `
    -Text $validateJob `
    -Pattern '(?m)^      contents: read\r?$' `
    -Scenario "Validation permissions"
Assert-Matches `
    -Text $validateJob `
    -Pattern '(?m)^          persist-credentials: false\r?$' `
    -Scenario "Read-only validation checkout"
Assert-Matches `
    -Text $validateJob `
    -Pattern '(?m)^          ref: main\r?$' `
    -Scenario "Main validation checkout"
Assert-Matches `
    -Text $validateJob `
    -Pattern '(?m)^          fetch-depth: 0\r?$' `
    -Scenario "Complete main history"
Assert-Matches `
    -Text $validateJob `
    -Pattern '(?m)^          \$tag = \./eng/assert-release-tag\.ps1 `\r?$' `
    -Scenario "Release-tag preflight"

Assert-Matches `
    -Text $candidateJob `
    -Pattern '(?m)^    needs: validate-tag\r?$' `
    -Scenario "Candidate validation dependency"
Assert-Matches `
    -Text $candidateJob `
    -Pattern '(?m)^      contents: read\r?$' `
    -Scenario "Candidate workflow permissions"
Assert-Matches `
    -Text $candidateJob `
    -Pattern '(?m)^    uses: \./\.github/workflows/release-candidate\.yml\r?$' `
    -Scenario "Reusable candidate gate"
Assert-NotMatches `
    -Text $candidateJob `
    -Pattern '(?m)^    secrets:' `
    -Scenario "Candidate secret forwarding"

Assert-Matches `
    -Text $dryRunJob `
    -Pattern '(?m)^    if: \$\{\{ inputs\.dry_run \}\}\r?$' `
    -Scenario "Dry-run condition"
Assert-Matches `
    -Text $dryRunJob `
    -Pattern '(?m)^      - verify-candidate\r?$' `
    -Scenario "Dry-run candidate gate"
Assert-Matches `
    -Text $dryRunJob `
    -Pattern '(?m)^    permissions: \{\}\r?$' `
    -Scenario "Dry-run permissions"

Assert-Matches `
    -Text $createJob `
    -Pattern '(?m)^    if: \$\{\{ ! inputs\.dry_run \}\}\r?$' `
    -Scenario "Real tag condition"
Assert-Matches `
    -Text $createJob `
    -Pattern '(?m)^      - verify-candidate\r?$' `
    -Scenario "Real tag candidate gate"
Assert-Matches `
    -Text $createJob `
    -Pattern '(?ms)^    environment:\r?\n      name: release\r?$' `
    -Scenario "Protected release environment"
Assert-Matches `
    -Text $createJob `
    -Pattern '(?m)^      contents: write\r?$' `
    -Scenario "Tag-creation permissions"
Assert-Matches `
    -Text $createJob `
    -Pattern '(?m)^          persist-credentials: true\r?$' `
    -Scenario "Tag-creation credentials"
Assert-Matches `
    -Text $createJob `
    -Pattern '(?m)^          ref: main\r?$' `
    -Scenario "Tag-creation main revalidation"
Assert-Matches `
    -Text $createJob `
    -Pattern '(?m)^          \$tag = \./eng/assert-release-tag\.ps1 `\r?$' `
    -Scenario "Tag-creation revalidation"
Assert-Matches `
    -Text $createJob `
    -Pattern '(?m)^          \$destination = "refs/tags/\$env:RELEASE_TAG"\r?$' `
    -Scenario "Derived tag destination"
Assert-Matches `
    -Text $createJob `
    -Pattern '(?m)^          \$refspec = ''\{0\}:\{1\}'' -f \$env:CANDIDATE_COMMIT, \$destination\r?$' `
    -Scenario "Exact commit-to-tag refspec"
Assert-Matches `
    -Text $createJob `
    -Pattern '(?ms)^          \$pushOutput = @\(\r?\n            & git push `\r?\n              --porcelain `\r?\n              --no-force `\r?\n              --no-follow-tags `\r?\n              --no-verify `\r?\n              origin `\r?\n              \$refspec 2>&1\)\r?$' `
    -Scenario "Create-only explicit tag push"
Assert-Matches `
    -Text $createJob `
    -Pattern '(?m)^            \$_\.StartsWith\("\*`t", \[System\.StringComparison\]::Ordinal\)\r?$' `
    -Scenario "New-ref push confirmation"
Assert-NotMatches `
    -Text $createJob `
    -Pattern '(?i)(?<!--no-)--force(?:\s|=)|force-with-lease|\+refs/tags|\bgit\s+tag\b|update-ref|delete' `
    -Scenario "Immutable tag creation"
Assert-Equal `
    -Actual ([regex]::Matches($workflow, '(?m)& git push `').Count) `
    -Expected 1 `
    -Scenario "Explicit tag push count"
Assert-Equal `
    -Actual ([regex]::Matches($workflow, 'actions/checkout@v6').Count) `
    -Expected 2 `
    -Scenario "Established checkout action major"

Assert-Matches `
    -Text $candidateWorkflow `
    -Pattern '(?m)^  workflow_call:\r?$' `
    -Scenario "Reusable candidate workflow"
Assert-Matches `
    -Text $candidateWorkflow `
    -Pattern '(?m)^      artifact-digest:\r?$' `
    -Scenario "Candidate artifact digest output"
Assert-Matches `
    -Text $candidateWorkflow `
    -Pattern '(?m)^      candidate-commit:\r?$' `
    -Scenario "Candidate commit output"
Assert-Matches `
    -Text $candidateWorkflow `
    -Pattern '(?m)^      candidate-version:\r?$' `
    -Scenario "Candidate version output"
Assert-NotMatches `
    -Text $candidateWorkflow `
    -Pattern '(?m)^\s+contents: write\r?$' `
    -Scenario "Reusable candidate permissions"

$temporaryRoot = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(),
    "dnaxi-release-tag-" + [System.Guid]::NewGuid().ToString("N"))
$workRepository = [System.IO.Path]::Combine($temporaryRoot, "work")
$remoteRepository = [System.IO.Path]::Combine($temporaryRoot, "remote.git")
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

try {
    Invoke-Git -Repository $temporaryRoot -Arguments @(
        "init",
        "--quiet",
        "--initial-branch=main",
        $workRepository
    ) | Out-Null

    $firstMainCommit = Add-FixtureCommit `
        -Repository $workRepository `
        -Content "first main" `
        -Message "first main"

    Invoke-Git -Repository $workRepository -Arguments @(
        "switch",
        "--quiet",
        "--create",
        "outside-main"
    ) | Out-Null
    $outsideMainCommit = Add-FixtureCommit `
        -Repository $workRepository `
        -Content "outside main" `
        -Message "outside main"

    Invoke-Git -Repository $workRepository -Arguments @(
        "switch",
        "--quiet",
        "main"
    ) | Out-Null
    $currentMainCommit = Add-FixtureCommit `
        -Repository $workRepository `
        -Content "current main" `
        -Message "current main"

    Invoke-Git -Repository $temporaryRoot -Arguments @(
        "init",
        "--bare",
        "--quiet",
        $remoteRepository
    ) | Out-Null
    Invoke-Git -Repository $workRepository -Arguments @(
        "remote",
        "add",
        "origin",
        $remoteRepository
    ) | Out-Null
    Invoke-Git -Repository $workRepository -Arguments @(
        "push",
        "--quiet",
        "origin",
        "refs/heads/main:refs/heads/main"
    ) | Out-Null
    Invoke-Git -Repository $workRepository -Arguments @(
        "fetch",
        "--quiet",
        "--no-tags",
        "origin",
        "refs/heads/main:refs/remotes/origin/main"
    ) | Out-Null

    Invoke-Git -Repository $remoteRepository -Arguments @(
        "update-ref",
        "refs/tags/v1.2.4",
        $currentMainCommit
    ) | Out-Null
    Invoke-Git -Repository $remoteRepository -Arguments @(
        "update-ref",
        "refs/tags/v1.2.5",
        $firstMainCommit
    ) | Out-Null
    Invoke-Git -Repository $remoteRepository -Arguments @(
        "update-ref",
        "refs/tags/v1.2.6/blocked",
        $firstMainCommit
    ) | Out-Null

    $initialTags = Invoke-Git -Repository $workRepository -Arguments @(
        "ls-remote",
        "--refs",
        "--tags",
        "origin"
    )
    $initialLocalRefs = Invoke-Git -Repository $workRepository -Arguments @(
        "for-each-ref",
        "--format=%(objectname) %(refname)"
    )

    $validResult = @(
        & $validatorPath `
            -CandidateCommit $currentMainCommit `
            -CandidateVersion "1.2.3-rc.1+build.5" `
            -RepositoryRoot $workRepository
    )
    Assert-Equal `
        -Actual $validResult.Count `
        -Expected 1 `
        -Scenario "Valid dry-run output count"
    Assert-Equal `
        -Actual $validResult[0] `
        -Expected "v1.2.3-rc.1+build.5" `
        -Scenario "Derived release tag"

    Assert-Refusal `
        -Scenario "Abbreviated commit" `
        -MessagePattern 'exact 40-character lowercase Git commit SHA' `
        -Action {
            & $validatorPath `
                -CandidateCommit $currentMainCommit.Substring(0, 12) `
                -CandidateVersion "1.2.3" `
                -RepositoryRoot $workRepository
        }
    Assert-Refusal `
        -Scenario "Missing exact commit" `
        -MessagePattern 'does not identify a local Git commit' `
        -Action {
            & $validatorPath `
                -CandidateCommit ("0" * 40) `
                -CandidateVersion "1.2.3" `
                -RepositoryRoot $workRepository
        }
    Assert-Refusal `
        -Scenario "Non-SemVer version" `
        -MessagePattern 'is not a valid SemVer 2\.0 version' `
        -Action {
            & $validatorPath `
                -CandidateCommit $currentMainCommit `
                -CandidateVersion "01.2.3" `
                -RepositoryRoot $workRepository
        }
    Assert-Refusal `
        -Scenario "Commit outside main" `
        -MessagePattern 'is not reachable from' `
        -Action {
            & $validatorPath `
                -CandidateCommit $outsideMainCommit `
                -CandidateVersion "1.2.3" `
                -RepositoryRoot $workRepository
        }
    Assert-Refusal `
        -Scenario "Existing release tag" `
        -MessagePattern 'already exists for candidate commit' `
        -Action {
            & $validatorPath `
                -CandidateCommit $currentMainCommit `
                -CandidateVersion "1.2.4" `
                -RepositoryRoot $workRepository
        }
    Assert-Refusal `
        -Scenario "Conflicting release tag" `
        -MessagePattern 'points to .* and conflicts with candidate commit' `
        -Action {
            & $validatorPath `
                -CandidateCommit $currentMainCommit `
                -CandidateVersion "1.2.5" `
                -RepositoryRoot $workRepository
        }
    Assert-Refusal `
        -Scenario "Conflicting tag namespace" `
        -MessagePattern 'conflicts with existing tag namespace' `
        -Action {
            & $validatorPath `
                -CandidateCommit $currentMainCommit `
                -CandidateVersion "1.2.6" `
                -RepositoryRoot $workRepository
        }

    $finalTags = Invoke-Git -Repository $workRepository -Arguments @(
        "ls-remote",
        "--refs",
        "--tags",
        "origin"
    )
    Assert-Equal `
        -Actual ($finalTags.Lines -join "`n") `
        -Expected ($initialTags.Lines -join "`n") `
        -Scenario "Dry-run remote tag refs"
    $finalLocalRefs = Invoke-Git -Repository $workRepository -Arguments @(
        "for-each-ref",
        "--format=%(objectname) %(refname)"
    )
    Assert-Equal `
        -Actual ($finalLocalRefs.Lines -join "`n") `
        -Expected ($initialLocalRefs.Lines -join "`n") `
        -Scenario "Dry-run local refs"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host (
    "Verified release-tag dry runs, refusal cases, candidate gate, " +
    "permissions, concurrency, environment, and immutable push controls.")
