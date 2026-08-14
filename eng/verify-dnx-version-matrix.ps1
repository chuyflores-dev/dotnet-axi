[CmdletBinding()]
param(
    [string] $RepositoryRoot,

    [string[]] $Versions = @("0.5.0", "0.5.0-alpha.1")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Captured {
    param(
        [Parameter(Mandatory)]
        [string] $FileName,

        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [Parameter(Mandatory)]
        [string] $WorkingDirectory,

        [hashtable] $Environment = @{},

        [TimeSpan] $Timeout = ([TimeSpan]::FromMinutes(10))
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    foreach ($entry in $Environment.GetEnumerator()) {
        $startInfo.Environment[[string] $entry.Key] = [string] $entry.Value
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Failed to start '$FileName'."
        }

        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit([int] $Timeout.TotalMilliseconds)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw (
                "'$FileName' timed out after " +
                "$($Timeout.TotalSeconds) seconds.")
        }

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $standardOutput.GetAwaiter().GetResult()
            StandardError = $standardError.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [System.IO.Path]::Combine($PSScriptRoot, "..")
}
$resolvedRepositoryRoot = (
    Resolve-Path -LiteralPath $RepositoryRoot
).Path

if ($Versions.Count -ne 2 -or
    ($Versions | Select-Object -Unique).Count -ne 2) {
    throw "The dnx matrix requires exactly two distinct versions."
}
$stableVersions = @($Versions | Where-Object { $_ -notmatch '-' })
$prereleaseVersions = @($Versions | Where-Object { $_ -match '-' })
if ($stableVersions.Count -ne 1 -or $prereleaseVersions.Count -ne 1) {
    throw "The dnx matrix requires one stable and one prerelease version."
}
foreach ($version in $Versions) {
    if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
        throw "Matrix version '$version' is not an exact package version."
    }
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$dnx = (Get-Command dnx -ErrorAction Stop).Source
$matrixRoot = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(),
    "dnaxi-dnx-matrix-" + [System.Guid]::NewGuid().ToString("N"))
$feedDirectory = [System.IO.Path]::Combine($matrixRoot, "feed")
[System.IO.Directory]::CreateDirectory($feedDirectory) | Out-Null

try {
    [xml] $nugetConfiguration = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="" />
  </packageSources>
</configuration>
'@
    $nugetConfiguration.configuration.packageSources.add.SetAttribute(
        "value",
        $feedDirectory)
    $nugetConfiguration.Save(
        [System.IO.Path]::Combine($matrixRoot, "NuGet.Config"))

    foreach ($version in $Versions) {
        $pack = Invoke-Captured `
            -FileName $dotnet `
            -Arguments @(
                "pack",
                "src/DotNetAxi.Cli/DotNetAxi.Cli.csproj",
                "--configuration", "Release",
                "--output", $feedDirectory,
                "--no-restore",
                "--disable-build-servers",
                "-p:DotNetAxiBuildVersion=$version") `
            -WorkingDirectory $resolvedRepositoryRoot
        if ($pack.ExitCode -ne 0) {
            throw (
                "Packing dnaxi $version failed. stderr: " +
                $pack.StandardError)
        }

        foreach ($extension in @("nupkg", "snupkg")) {
            $packagePath = [System.IO.Path]::Combine(
                $feedDirectory,
                "dnaxi.$version.$extension")
            if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
                throw "Expected matrix package '$packagePath' is missing."
            }
        }
    }

    foreach ($version in $Versions) {
        $cliHome = [System.IO.Path]::Combine(
            $matrixRoot,
            "cli-home-$version")
        $dnxEnvironment = @{
            DOTNET_CLI_HOME = $cliHome
            DOTNET_CLI_TELEMETRY_OPTOUT = "1"
            DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "1"
            DOTNET_NOLOGO = "1"
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
            NUGET_PACKAGES = [System.IO.Path]::Combine(
                $matrixRoot,
                "packages-$version")
        }
        $result = Invoke-Captured `
            -FileName $dnx `
            -Arguments @(
                "dnaxi@$version",
                "--source", $feedDirectory,
                "--no-http-cache",
                "--verbosity", "quiet",
                "--", "--version") `
            -WorkingDirectory $matrixRoot `
            -Environment $dnxEnvironment
        if ($result.ExitCode -ne 0) {
            throw (
                "dnx dnaxi@$version failed. stderr: " +
                $result.StandardError)
        }
        if (-not $result.StandardOutput.StartsWith(
                "schema: dotnet-axi/v1`n",
                [System.StringComparison]::Ordinal)) {
            throw "dnx dnaxi@$version stdout is not a clean structured document."
        }

        $versionLines = @(
            $result.StandardOutput -split "`n" |
                Where-Object { $_ -cmatch '^tool_version: .+$' }
        )
        if ($versionLines.Count -ne 1 -or
            $versionLines[0] -cne "tool_version: $version") {
            throw "dnx dnaxi@$version did not report the exact version."
        }

        $unexpectedPersistentCommand = [System.IO.Path]::Combine(
            $cliHome,
            ".dotnet",
            "tools",
            $(if ([System.OperatingSystem]::IsWindows()) {
                "dnaxi.exe"
            }
            else {
                "dnaxi"
            }))
        if (Test-Path -LiteralPath $unexpectedPersistentCommand) {
            throw "dnx dnaxi@$version created a persistent command."
        }
    }
}
finally {
    if (Test-Path -LiteralPath $matrixRoot) {
        Remove-Item -LiteralPath $matrixRoot -Recurse -Force
    }
}

Write-Host (
    "Verified exact stable and prerelease dnx execution for dnaxi: " +
    ($Versions -join ", ") + ".")
