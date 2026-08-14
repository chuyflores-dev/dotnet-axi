#!/usr/bin/env -S pwsh -NoLogo -NoProfile

<#
.SYNOPSIS
Runs one real repository task with Codex and grades the resulting workspace.

.EXAMPLE
./eng/benchmark-agent.ps1 -ListTasks

.EXAMPLE
./eng/benchmark-agent.ps1 -Task add-ledger-try-format

.EXAMPLE
./eng/benchmark-agent.ps1 -Condition baseline -Task refactor-owned-scope-probe
#>

[CmdletBinding()]
param(
    [ValidateSet('candidate', 'baseline')]
    [string]$Condition = 'candidate',

    [string]$Task = 'add-ledger-try-format',

    [string]$Corpus,

    [string]$PackageFeed,

    [string]$ProductVersion = '0.5.0',

    [string]$Model = 'gpt-5.6-luna',

    [string]$Reasoning = 'low',

    [string]$CodexExecutable = 'codex',

    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 600,

    [string]$ResultsDirectory,

    [switch]$OuterIsolated,

    [switch]$ListTasks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Corpus)) {
    $Corpus = Join-Path $repoRoot `
        'tests/Fixtures/AgentTasks/repository-work/corpus.json'
}
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $repoRoot 'artifacts/agent-benchmark'
}

$Corpus = [IO.Path]::GetFullPath($Corpus)
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
$corpusDirectory = Split-Path -Parent $Corpus
$corpusDocument = Get-Content -Raw -LiteralPath $Corpus | ConvertFrom-Json

if ($ListTasks) {
    $corpusDocument.tasks |
        Select-Object id, milestone, kind |
        Format-Table -AutoSize
    return
}

$taskDefinition = @($corpusDocument.tasks | Where-Object id -EQ $Task)
if ($taskDefinition.Count -ne 1) {
    throw "Task '$Task' was not found exactly once in '$Corpus'."
}
$taskDefinition = $taskDefinition[0]
if (-not $taskDefinition.applicability.$Condition) {
    throw "Task '$Task' does not apply to condition '$Condition'."
}
$allowedChanges = @($taskDefinition.allowedChanges |
    ForEach-Object { ([string]$_).Replace('\', '/') })
if ($allowedChanges.Count -eq 0) {
    throw "Task '$Task' must allow at least one repository change."
}
if (@($taskDefinition.validation.command).Count -eq 0) {
    throw "Task '$Task' must declare a validation command."
}

foreach ($command in @($CodexExecutable, 'git', 'jq', 'dotnet')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command '$command' was not found."
    }
}

if ($Condition -eq 'candidate') {
    if (-not (Get-Command dnx -ErrorAction SilentlyContinue)) {
        throw "Required command 'dnx' was not found."
    }
    if ([string]::IsNullOrWhiteSpace($PackageFeed)) {
        $stamp = (Get-Date).ToUniversalTime().ToString(
            'yyyyMMddTHHmmssZ').ToLowerInvariant()
        $ProductVersion = "$ProductVersion-bench.$stamp.$PID"
        Write-Host "Packing current source as $ProductVersion..."
        $packOutput = @(& (Join-Path $PSScriptRoot `
            'pack-local-candidate.ps1') -Version $ProductVersion)
        if ($packOutput.Count -eq 0) {
            throw 'Could not pack the current candidate.'
        }
        $PackageFeed = [string]$packOutput[-1]
    }
    $PackageFeed = [IO.Path]::GetFullPath($PackageFeed)
    $package = Join-Path $PackageFeed "dnaxi.$ProductVersion.nupkg"
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Candidate package '$package' does not exist."
    }
}

$manifestPath = Join-Path $corpusDirectory `
    $taskDefinition.repository.fixtureManifest
$manifestPath = [IO.Path]::GetFullPath($manifestPath)
$manifestDirectory = Split-Path -Parent $manifestPath
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$codexCommand = Get-Command $CodexExecutable
$codexVersion = (& $codexCommand.Source --version 2>$null | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($codexVersion)) {
    throw "Could not read the Codex version from '$($codexCommand.Source)'."
}
if ($OuterIsolated) {
    Write-Warning (
        'Codex sandboxing is disabled. Run only inside a disposable outer ' +
        'VM or container containing the benchmark inputs and authentication.')
}

$runId = '{0}-{1}-{2}-{3}' -f `
    (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'), `
    $Condition, `
    $Task, `
    $PID
$artifactDirectory = Join-Path $ResultsDirectory $runId
$workspace = Join-Path ([IO.Path]::GetTempPath()) "dnaxi-bench-$runId"
$baselineCommandDirectory = $null
$utf8 = [Text.UTF8Encoding]::new($false)

New-Item -ItemType Directory -Path $artifactDirectory | Out-Null
New-Item -ItemType Directory -Path $workspace | Out-Null

try {
    foreach ($file in $manifest.files) {
        if ($file.expandTokens) {
            throw "Fixture '$manifestPath' requires token expansion."
        }
        $source = Join-Path $manifestDirectory $file.template
        $destination = Join-Path $workspace $file.path
        New-Item -ItemType Directory -Force `
            -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }

    $benchmarkDirectory = Join-Path $workspace '.benchmark'
    New-Item -ItemType Directory -Path $benchmarkDirectory | Out-Null
    if ($Condition -eq 'candidate') {
        $skillDestination = Join-Path $workspace '.agents/skills/dotnet-axi'
        New-Item -ItemType Directory -Path $skillDestination | Out-Null
        Copy-Item -Recurse -Path (Join-Path $repoRoot 'skills/dotnet-axi/*') `
            -Destination $skillDestination
        $genericPrefix =
            "dnx dnaxi@$ProductVersion --verbosity quiet --"
        $sourcePinnedPrefix =
            "dnx dnaxi@$ProductVersion --source `"`$DNAXI_LOCAL_FEED`" " +
            '--verbosity quiet --'
        foreach ($skillDocument in Get-ChildItem `
            -LiteralPath $skillDestination `
            -Recurse `
            -File `
            -Filter '*.md') {
            $candidateSkill = [IO.File]::ReadAllText(
                $skillDocument.FullName).Replace(
                    'dnaxi@0.5.0',
                    "dnaxi@$ProductVersion").Replace(
                        $genericPrefix,
                        $sourcePinnedPrefix)
            [IO.File]::WriteAllText(
                $skillDocument.FullName,
                $candidateSkill,
                $utf8)
        }
        $feedDestination = Join-Path $benchmarkDirectory 'feed'
        New-Item -ItemType Directory -Path $feedDestination | Out-Null
        Copy-Item -LiteralPath $package -Destination $feedDestination
        Push-Location $workspace
        try {
            & dnx "dnaxi@$ProductVersion" --source $feedDestination `
                --verbosity quiet -- --version | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw 'The candidate package preflight failed.'
            }
        }
        finally {
            Pop-Location
        }
    }

    [IO.File]::WriteAllText(
        (Join-Path $workspace '.gitignore'),
        ".agents/`n.benchmark/`n.benchmark-validation/`n**/bin/`n**/obj/`n",
        $utf8)
    & git -C $workspace init --quiet
    & git -C $workspace add --all
    & git -C $workspace `
        -c user.name='dotnet-axi benchmark' `
        -c user.email='benchmark@dotnet-axi.invalid' `
        -c commit.gpgSign=false `
        commit --quiet -m fixture
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the benchmark fixture commit.'
    }
    $fixtureCommit = (& git -C $workspace rev-parse HEAD).Trim()

    $finalPath = Join-Path $benchmarkDirectory 'final.txt'
    $prompt = [string]$taskDefinition.prompt + "`n`n" +
        'Complete the task in the repository; do not merely describe a ' +
        'solution. Run useful validation before finishing.'
    $eventsPath = Join-Path $artifactDirectory 'events.jsonl'
    $stderrPath = Join-Path $artifactDirectory 'stderr.txt'

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $codexCommand.Source
    $startInfo.WorkingDirectory = $workspace
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
        'exec', '--ephemeral', '--json', '--ignore-user-config',
        '--ignore-rules', '--skip-git-repo-check', '--model', $Model,
        '--cd', $workspace
    )) {
        $startInfo.ArgumentList.Add($argument)
    }
    if ($OuterIsolated) {
        $startInfo.ArgumentList.Add(
            '--dangerously-bypass-approvals-and-sandbox')
    }
    else {
        $startInfo.ArgumentList.Add('--sandbox')
        $startInfo.ArgumentList.Add('workspace-write')
    }
    foreach ($argument in @(
        '--output-last-message', $finalPath,
        '--config', "model_reasoning_effort=`"$Reasoning`"",
        '--config', 'approval_policy="never"',
        '--config', 'web_search="disabled"',
        '--', $prompt
    )) {
        $startInfo.ArgumentList.Add($argument)
    }
    if ($Condition -eq 'candidate') {
        $startInfo.Environment['DNAXI_LOCAL_FEED'] =
            (Join-Path $benchmarkDirectory 'feed')
    }
    else {
        $startInfo.Environment.Remove('DNAXI_LOCAL_FEED') | Out-Null
        $baselineCommandDirectory = Join-Path `
            ([IO.Path]::GetTempPath()) "benchmark-path-$runId"
        New-Item -ItemType Directory `
            -Path $baselineCommandDirectory | Out-Null
        foreach ($command in @('dnx', 'dnaxi', 'dotnet-dnaxi')) {
            if ($IsWindows) {
                $blockedCommand = Join-Path `
                    $baselineCommandDirectory "$command.cmd"
                [IO.File]::WriteAllText(
                    $blockedCommand,
                    "@echo off`r`necho $command is unavailable in the baseline. 1>&2`r`nexit /b 127`r`n",
                    $utf8)
            }
            else {
                $blockedCommand = Join-Path $baselineCommandDirectory $command
                [IO.File]::WriteAllText(
                    $blockedCommand,
                    "#!/bin/sh`necho '$command is unavailable in the baseline.' >&2`nexit 127`n",
                    $utf8)
                & chmod +x $blockedCommand
                if ($LASTEXITCODE -ne 0) {
                    throw "Could not block '$command' for the baseline."
                }
            }
        }
        $startInfo.Environment['PATH'] = $baselineCommandDirectory +
            [IO.Path]::PathSeparator + $startInfo.Environment['PATH']
    }
    $startInfo.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $startInfo.Environment['DOTNET_NOLOGO'] = '1'
    $startInfo.Environment['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'

    Write-Host "Running $Condition/$Task with $Model ($Reasoning)..."
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Codex did not start.'
    }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) {
        $process.Kill($true)
        $process.WaitForExit()
    }
    $stopwatch.Stop()
    $events = $stdoutTask.GetAwaiter().GetResult()
    $standardError = $stderrTask.GetAwaiter().GetResult()
    [IO.File]::WriteAllText($eventsPath, $events, $utf8)
    [IO.File]::WriteAllText($stderrPath, $standardError, $utf8)

    $metrics = & jq -s --arg version $ProductVersion '
      def tools: ["command_execution","file_change","mcp_tool_call","web_search"];
      {
        inputTokens: ([.[] | select(.type=="turn.completed") | .usage.input_tokens // 0] | add // 0),
        cachedInputTokens: ([.[] | select(.type=="turn.completed") | .usage.cached_input_tokens // 0] | add // 0),
        cacheWriteInputTokens: ([.[] | select(.type=="turn.completed") | .usage.cache_write_input_tokens // 0] | add // 0),
        outputTokens: ([.[] | select(.type=="turn.completed") | .usage.output_tokens // 0] | add // 0),
        reasoningOutputTokens: ([.[] | select(.type=="turn.completed") | .usage.reasoning_output_tokens // 0] | add // 0),
        turns: ([.[] | select(.type=="turn.started")] | length),
        toolCalls: ([.[] | select((.type=="item.started" or .type=="item.completed") and (.item.type as $t | tools | index($t))) | .item.id] | unique | length),
        dnaxiInvocations: ([.[] | select((.type=="item.started" or .type=="item.completed") and .item.type=="command_execution" and (.item.command | contains("dnx dnaxi@" + $version))) | .item.id] | unique | length),
        dnaxiNonzeroExits: ([.[] | select(.type=="item.completed" and .item.type=="command_execution" and (.item.command | contains("dnx dnaxi@" + $version)) and (.item.exit_code != 0)) | .item.id] | unique | length)
      }' $eventsPath | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        throw 'Codex JSONL could not be summarized.'
    }

    if (Test-Path -LiteralPath $finalPath -PathType Leaf) {
        Copy-Item -LiteralPath $finalPath `
            -Destination (Join-Path $artifactDirectory 'final.txt')
    }

    $changedFiles = @(
        @(
            & git -C $workspace diff $fixtureCommit --name-only --no-renames --
            & git -C $workspace ls-files --others --exclude-standard
        ) | ForEach-Object { ([string]$_).Trim().Replace('\', '/') } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
    $unexpectedChanges = @($changedFiles |
        Where-Object { $_ -notin $allowedChanges })
    $changesAllowed = $changedFiles.Count -gt 0 -and
        $unexpectedChanges.Count -eq 0

    & git -C $workspace add --intent-to-add --all
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not prepare the complete benchmark diff.'
    }
    $repositoryDiff = (& git -C $workspace diff $fixtureCommit --binary `
        --full-index --no-ext-diff --no-renames -- | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not capture the complete benchmark diff.'
    }
    [IO.File]::WriteAllText(
        (Join-Path $artifactDirectory 'changes.patch'),
        $repositoryDiff,
        $utf8)

    $completed = -not $timedOut -and $process.ExitCode -eq 0
    $validationPassed = $false
    $validationExitCode = $null
    $validationDurationSeconds = 0
    if ($completed -and $changesAllowed) {
        $validationRoot = Join-Path $workspace '.benchmark-validation'
        if (Test-Path -LiteralPath $validationRoot) {
            Remove-Item -LiteralPath $validationRoot -Recurse -Force
        }
        foreach ($file in $taskDefinition.validation.files) {
            $source = [IO.Path]::GetFullPath(
                (Join-Path $corpusDirectory $file.template))
            $destination = Join-Path $workspace $file.path
            New-Item -ItemType Directory -Force `
                -Path (Split-Path -Parent $destination) | Out-Null
            Copy-Item -LiteralPath $source -Destination $destination
        }

        $validationCommand = @($taskDefinition.validation.command |
            ForEach-Object { [string]$_ })
        $validationInfo = [Diagnostics.ProcessStartInfo]::new()
        $validationInfo.FileName = $validationCommand[0]
        $validationInfo.WorkingDirectory = $workspace
        $validationInfo.UseShellExecute = $false
        $validationInfo.RedirectStandardOutput = $true
        $validationInfo.RedirectStandardError = $true
        foreach ($argument in $validationCommand[1..($validationCommand.Count - 1)]) {
            $validationInfo.ArgumentList.Add($argument)
        }
        $validationInfo.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
        $validationInfo.Environment['DOTNET_NOLOGO'] = '1'
        $validationStopwatch = [Diagnostics.Stopwatch]::StartNew()
        $validationProcess = [Diagnostics.Process]::Start($validationInfo)
        if ($null -eq $validationProcess) {
            throw 'Validation did not start.'
        }
        $validationOutputTask = $validationProcess.StandardOutput.ReadToEndAsync()
        $validationErrorTask = $validationProcess.StandardError.ReadToEndAsync()
        $validationTimedOut = -not $validationProcess.WaitForExit(
            [int]$taskDefinition.validation.timeoutSeconds * 1000)
        if ($validationTimedOut) {
            $validationProcess.Kill($true)
            $validationProcess.WaitForExit()
        }
        $validationStopwatch.Stop()
        $validationExitCode = if ($validationTimedOut) {
            $null
        }
        else {
            $validationProcess.ExitCode
        }
        $validationDurationSeconds = [Math]::Round(
            $validationStopwatch.Elapsed.TotalSeconds,
            3)
        $validationOutput = $validationOutputTask.GetAwaiter().GetResult() +
            $validationErrorTask.GetAwaiter().GetResult()
        [IO.File]::WriteAllText(
            (Join-Path $artifactDirectory 'validation.txt'),
            $validationOutput,
            $utf8)
        $validationPassed = -not $validationTimedOut -and
            $validationExitCode -eq 0
    }

    $successfulDnaxiInvocations = [Math]::Max(
        0,
        $metrics.dnaxiInvocations - $metrics.dnaxiNonzeroExits)
    $activated = $metrics.dnaxiInvocations -gt 0
    $passed = $completed -and $changesAllowed -and $validationPassed
    $failureKind = if ($passed) {
        'none'
    }
    elseif ($timedOut) {
        'timeout'
    }
    elseif (-not $completed -and $metrics.turns -eq 0) {
        'harness'
    }
    elseif (-not $completed) {
        'agent'
    }
    elseif (-not $changesAllowed) {
        'scope'
    }
    else {
        'validation'
    }

    $result = [ordered]@{
        schema = 'dotnet-axi/agent-benchmark-run/v2'
        runId = $runId
        condition = $Condition
        task = $Task
        taskKind = [string]$taskDefinition.kind
        codexVersion = $codexVersion
        model = $Model
        reasoning = $Reasoning
        productVersion = if ($Condition -eq 'candidate') {
            $ProductVersion
        } else {
            $null
        }
        passed = $passed
        completed = $completed
        timedOut = $timedOut
        changesAllowed = $changesAllowed
        diffArtifact = 'changes.patch'
        changedFiles = $changedFiles
        unexpectedChanges = $unexpectedChanges
        validationPassed = $validationPassed
        validationExitCode = $validationExitCode
        validationDurationSeconds = $validationDurationSeconds
        failureKind = $failureKind
        dnaxiActivated = $activated
        dnaxiInvocationCount = $metrics.dnaxiInvocations
        dnaxiSuccessfulInvocationCount = $successfulDnaxiInvocations
        dnaxiNonzeroExitCount = $metrics.dnaxiNonzeroExits
        inputTokens = $metrics.inputTokens
        cachedInputTokens = $metrics.cachedInputTokens
        cacheWriteInputTokens = $metrics.cacheWriteInputTokens
        freshInputTokens = [Math]::Max(
            0,
            $metrics.inputTokens - $metrics.cachedInputTokens)
        outputTokens = $metrics.outputTokens
        reasoningOutputTokens = $metrics.reasoningOutputTokens
        totalTokens = $metrics.inputTokens + $metrics.outputTokens
        turns = $metrics.turns
        toolCalls = $metrics.toolCalls
        durationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
        exitCode = if ($timedOut) { $null } else { $process.ExitCode }
    }
    $resultJson = $result | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText(
        (Join-Path $artifactDirectory 'result.json'),
        $resultJson + "`n",
        $utf8)
    New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
    [IO.File]::AppendAllText(
        (Join-Path $ResultsDirectory 'results.jsonl'),
        ($result | ConvertTo-Json -Compress -Depth 5) + "`n",
        $utf8)

    $result | Format-List
    Write-Host "Evidence: $artifactDirectory"
    if (-not $passed) {
        exit 1
    }
}
finally {
    if (Test-Path -LiteralPath $workspace) {
        Remove-Item -LiteralPath $workspace -Recurse -Force
    }
    if ($null -ne $baselineCommandDirectory -and
        (Test-Path -LiteralPath $baselineCommandDirectory)) {
        Remove-Item -LiteralPath $baselineCommandDirectory -Recurse -Force
    }
}
