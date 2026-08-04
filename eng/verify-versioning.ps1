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

$temporaryRoot = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(),
    "dnaxi-versioning-" + [System.Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

try {
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::Combine($temporaryRoot, "fixture.txt"),
        "dotnet-axi`n",
        [System.Text.UTF8Encoding]::new($false))
    Invoke-Git -Repository $temporaryRoot -Arguments @("init", "--quiet")
    Invoke-Git -Repository $temporaryRoot -Arguments @("add", "fixture.txt")
    Add-ProbeCommit -Repository $temporaryRoot -Message "initial"
    Add-ProbeCommit `
        -Repository $temporaryRoot `
        -Message "untagged" `
        -AllowEmpty

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
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host "Verified tag-derived, candidate, and post-release versions."
