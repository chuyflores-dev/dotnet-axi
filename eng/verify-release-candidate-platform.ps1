[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [Parameter(Mandatory)]
    [string] $ExpectedCommit,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [Parameter(Mandatory)]
    [string] $EvidencePath,

    [string] $RunnerOs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-CommandPath {
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [hashtable] $Environment = @{}
    )

    if (-not [string]::IsNullOrEmpty(
            [System.IO.Path]::GetDirectoryName($Command))) {
        return $Command
    }

    $searchPath = if ($Environment.ContainsKey("PATH")) {
        [string] $Environment["PATH"]
    }
    else {
        [System.Environment]::GetEnvironmentVariable("PATH")
    }
    $extensions = @("", ".exe", ".cmd", ".bat")
    foreach ($directory in $searchPath -split [System.IO.Path]::PathSeparator) {
        if ([string]::IsNullOrWhiteSpace($directory)) {
            continue
        }

        foreach ($extension in $extensions) {
            $candidate = [System.IO.Path]::Combine(
                $directory,
                $Command + $extension)
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return $candidate
            }
        }
    }

    throw "Command '$Command' was not found on the selected PATH."
}

function Invoke-Captured {
    param(
        [Parameter(Mandatory)]
        [string] $FileName,

        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [hashtable] $Environment = @{},

        [string] $WorkingDirectory,

        [TimeSpan] $Timeout = [TimeSpan]::FromMinutes(2)
    )

    $resolvedFileName = Resolve-CommandPath `
        -Command $FileName `
        -Environment $Environment
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $isCommandScript = (
        [System.OperatingSystem]::IsWindows() -and
        [System.IO.Path]::GetExtension($resolvedFileName) -in
            @(".cmd", ".bat")
    )
    if ($isCommandScript) {
        $startInfo.FileName = [System.IO.Path]::Combine(
            [System.Environment]::GetFolderPath(
                [System.Environment+SpecialFolder]::System),
            "cmd.exe")
        $startInfo.ArgumentList.Add("/d")
        $startInfo.ArgumentList.Add("/c")
        $startInfo.ArgumentList.Add($resolvedFileName)
    }
    else {
        $startInfo.FileName = $resolvedFileName
    }
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    $startInfo.Environment["DOTNET_NOLOGO"] = "1"
    $startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $startInfo.WorkingDirectory = $WorkingDirectory
    }
    foreach ($name in $Environment.Keys) {
        $startInfo.Environment[$name] = [string] $Environment[$name]
    }
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
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
            throw "'$FileName' timed out after $($Timeout.TotalSeconds) seconds."
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

function Assert-Success {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Result,

        [Parameter(Mandatory)]
        [string] $Operation
    )

    if ($Result.ExitCode -ne 0) {
        throw (
            "$Operation exited $($Result.ExitCode). stderr: " +
            $Result.StandardError)
    }
}

function Get-ObservedVersion {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Result,

        [Parameter(Mandatory)]
        [string] $Operation
    )

    Assert-Success -Result $Result -Operation $Operation
    if (-not $Result.StandardOutput.StartsWith(
            "schema: dotnet-axi/v1`n",
            [System.StringComparison]::Ordinal)) {
        throw "$Operation stdout is contaminated before the structured document."
    }
    $lines = $Result.StandardOutput -split "`n"
    foreach ($requiredLine in @(
            "schema: dotnet-axi/v1",
            "command: version",
            "status: success",
            "tool: dotnet-axi")) {
        if ($lines -notcontains $requiredLine) {
            throw "$Operation output is missing '$requiredLine'."
        }
    }

    $versionLines = @(
        $lines | Where-Object { $_ -cmatch '^tool_version: .+$' }
    )
    if ($versionLines.Count -ne 1) {
        throw "$Operation did not report exactly one tool_version."
    }

    return $versionLines[0].Substring("tool_version: ".Length)
}

& ([System.IO.Path]::Combine(
        $PSScriptRoot,
        "assert-release-candidate-inputs.ps1")) `
    -CandidateCommit $ExpectedCommit `
    -CandidateVersion $ExpectedVersion

$resolvedPackageDirectory = (
    Resolve-Path -LiteralPath $PackageDirectory
).Path
$packages = @(
    Get-ChildItem -LiteralPath $resolvedPackageDirectory -File |
        Where-Object {
            $_.Name -like "dnaxi.*.nupkg" -and
            $_.Name -notlike "*.snupkg"
        }
)
if ($packages.Count -ne 1) {
    throw (
        "Expected one dnaxi .nupkg in '$resolvedPackageDirectory'; " +
        "found $($packages.Count).")
}
$package = $packages[0]
$symbolPackagePath = [System.IO.Path]::ChangeExtension(
    $package.FullName,
    ".snupkg")
if (-not (Test-Path -LiteralPath $symbolPackagePath -PathType Leaf)) {
    throw "Symbol package '$symbolPackagePath' is missing."
}

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
    $reader = [System.IO.StreamReader]::new($stream)
    try {
        [xml] $nuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }

    $packageVersion = [string] $nuspec.package.metadata.version
    $packageCommit = [string] $nuspec.package.metadata.repository.commit
    $packageId = [string] $nuspec.package.metadata.id
    if ($packageId -cne "dnaxi") {
        throw "Package ID is '$packageId', expected 'dnaxi'."
    }
    if ($packageVersion -cne $ExpectedVersion) {
        throw "Package version is '$packageVersion', expected '$ExpectedVersion'."
    }
    if ($packageCommit -cne $ExpectedCommit) {
        throw "Package commit is '$packageCommit', expected '$ExpectedCommit'."
    }
}
finally {
    $archive.Dispose()
}

$symbolArchive = [System.IO.Compression.ZipFile]::OpenRead(
    $symbolPackagePath)
try {
    $symbolNuspecEntries = @(
        $symbolArchive.Entries |
            Where-Object { $_.FullName -like "*.nuspec" }
    )
    if ($symbolNuspecEntries.Count -ne 1) {
        throw (
            "Expected one symbol-package nuspec entry; found " +
            "$($symbolNuspecEntries.Count).")
    }

    $stream = $symbolNuspecEntries[0].Open()
    $reader = [System.IO.StreamReader]::new($stream)
    try {
        [xml] $symbolNuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }

    $symbolMetadata = $symbolNuspec.package.metadata
    if ([string] $symbolMetadata.id -cne "dnaxi") {
        throw (
            "Symbol package ID is '$($symbolMetadata.id)', " +
            "expected 'dnaxi'.")
    }
    if ([string] $symbolMetadata.version -cne $ExpectedVersion) {
        throw (
            "Symbol package version is '$($symbolMetadata.version)', " +
            "expected '$ExpectedVersion'.")
    }
    if ([string] $symbolMetadata.repository.commit -cne $ExpectedCommit) {
        throw (
            "Symbol package repository commit is " +
            "'$($symbolMetadata.repository.commit)', expected " +
            "'$ExpectedCommit'.")
    }
    if ([string] $symbolMetadata.packageTypes.packageType.name -cne
        "SymbolsPackage") {
        throw "Symbol package type must be SymbolsPackage."
    }
}
finally {
    $symbolArchive.Dispose()
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$dnx = (Get-Command dnx -ErrorAction Stop).Source
$temporaryRoot = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(),
    "dnaxi-candidate-smoke-" + [System.Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

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
        $resolvedPackageDirectory)
    $nugetConfigPath = [System.IO.Path]::Combine(
        $temporaryRoot,
        "NuGet.Config")
    $nugetConfiguration.Save($nugetConfigPath)

    $dnxFirstEnvironment = @{
        DOTNET_CLI_HOME = [System.IO.Path]::Combine(
            $temporaryRoot,
            "dnx-first-home")
        DOTNET_CLI_TELEMETRY_OPTOUT = "1"
        DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "1"
        DOTNET_NOLOGO = "1"
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
        NUGET_PACKAGES = [System.IO.Path]::Combine(
            $temporaryRoot,
            "dnx-first-packages")
    }
    $dnxFirstResult = Invoke-Captured `
        -FileName $dnx `
        -Arguments @(
            "dnaxi@$ExpectedVersion",
            "--source", $resolvedPackageDirectory,
            "--no-http-cache", "--verbosity", "quiet",
            "--", "--version") `
        -Environment $dnxFirstEnvironment `
        -WorkingDirectory $temporaryRoot
    $dnxFirstVersion = Get-ObservedVersion `
        -Result $dnxFirstResult `
        -Operation "Dnx-first candidate"
    if ($dnxFirstVersion -cne $ExpectedVersion) {
        throw (
            "Dnx-first candidate reported '$dnxFirstVersion', " +
            "expected '$ExpectedVersion'.")
    }

    $globalCliHome = [System.IO.Path]::Combine($temporaryRoot, "global-home")
    $globalEnvironment = @{
        DOTNET_CLI_HOME = $globalCliHome
        NUGET_PACKAGES = [System.IO.Path]::Combine(
            $temporaryRoot,
            "global-packages")
        NUGET_HTTP_CACHE_PATH = [System.IO.Path]::Combine(
            $temporaryRoot,
            "global-http-cache")
        NUGET_PLUGINS_CACHE_PATH = [System.IO.Path]::Combine(
            $temporaryRoot,
            "global-plugin-cache")
    }
    $globalInstall = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool", "install", "--global", "dnaxi",
            "--version", $ExpectedVersion,
            "--configfile", $nugetConfigPath,
            "--no-http-cache", "--verbosity", "quiet") `
        -Environment $globalEnvironment
    Assert-Success -Result $globalInstall -Operation "Global tool installation"

    $executableName = if ([System.OperatingSystem]::IsWindows()) {
        "dnaxi.exe"
    }
    else {
        "dnaxi"
    }
    $globalToolDirectory = [System.IO.Path]::Combine(
        $globalCliHome,
        ".dotnet",
        "tools")
    $globalExecutable = [System.IO.Path]::Combine(
        $globalToolDirectory,
        $executableName)
    if (-not (Test-Path -LiteralPath $globalExecutable -PathType Leaf)) {
        throw "Installed global command '$globalExecutable' is missing."
    }
    $globalEnvironment["PATH"] = (
        $globalToolDirectory +
        [System.IO.Path]::PathSeparator +
        [System.Environment]::GetEnvironmentVariable("PATH"))
    $globalResult = Invoke-Captured `
        -FileName "dnaxi" `
        -Arguments @("--version") `
        -Environment $globalEnvironment
    $globalVersion = Get-ObservedVersion `
        -Result $globalResult `
        -Operation "Global version invocation"

    $localWorkspace = [System.IO.Path]::Combine(
        $temporaryRoot,
        "local-workspace")
    [System.IO.Directory]::CreateDirectory($localWorkspace) | Out-Null
    $localEnvironment = @{
        DOTNET_CLI_HOME = [System.IO.Path]::Combine(
            $temporaryRoot,
            "local-home")
        NUGET_PACKAGES = [System.IO.Path]::Combine(
            $temporaryRoot,
            "local-packages")
        NUGET_HTTP_CACHE_PATH = [System.IO.Path]::Combine(
            $temporaryRoot,
            "local-http-cache")
        NUGET_PLUGINS_CACHE_PATH = [System.IO.Path]::Combine(
            $temporaryRoot,
            "local-plugin-cache")
    }
    $manifestCreation = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @("new", "tool-manifest", "--no-update-check") `
        -Environment $localEnvironment `
        -WorkingDirectory $localWorkspace
    Assert-Success `
        -Result $manifestCreation `
        -Operation "Local tool manifest creation"
    $manifests = @(
        Get-ChildItem `
            -LiteralPath $localWorkspace `
            -Filter "dotnet-tools.json" `
            -File `
            -Recurse
    )
    if ($manifests.Count -ne 1) {
        throw "Expected one local tool manifest; found $($manifests.Count)."
    }
    $manifestPath = $manifests[0].FullName
    $localInstall = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool", "install", "--local", "dnaxi",
            "--tool-manifest", $manifestPath,
            "--version", $ExpectedVersion,
            "--configfile", $nugetConfigPath,
            "--no-http-cache", "--verbosity", "quiet") `
        -Environment $localEnvironment `
        -WorkingDirectory $localWorkspace
    Assert-Success -Result $localInstall -Operation "Local tool installation"
    $localResult = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @("tool", "run", "dnaxi", "--", "--version") `
        -Environment $localEnvironment `
        -WorkingDirectory $localWorkspace
    $localVersion = Get-ObservedVersion `
        -Result $localResult `
        -Operation "Local version invocation"

    $dnxCliHome = [System.IO.Path]::Combine($temporaryRoot, "dnx-home")
    $dnxEnvironment = @{
        DOTNET_CLI_HOME = $dnxCliHome
        DOTNET_CLI_TELEMETRY_OPTOUT = "1"
        DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "1"
        DOTNET_NOLOGO = "1"
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
        NUGET_PACKAGES = [System.IO.Path]::Combine(
            $temporaryRoot,
            "dnx-packages")
        NUGET_HTTP_CACHE_PATH = [System.IO.Path]::Combine(
            $temporaryRoot,
            "dnx-http-cache")
        NUGET_PLUGINS_CACHE_PATH = [System.IO.Path]::Combine(
            $temporaryRoot,
            "dnx-plugin-cache")
    }
    $dnxResult = Invoke-Captured `
        -FileName $dnx `
        -Arguments @(
            "dnaxi@$ExpectedVersion",
            "--source", $resolvedPackageDirectory,
            "--no-http-cache", "--verbosity", "quiet",
            "--", "--version") `
        -Environment $dnxEnvironment `
        -WorkingDirectory $temporaryRoot
    $dnxVersion = Get-ObservedVersion `
        -Result $dnxResult `
        -Operation "dnx version invocation"

    foreach ($observed in @($globalVersion, $localVersion, $dnxVersion)) {
        if ($observed -cne $ExpectedVersion) {
            throw "Observed tool version '$observed', expected '$ExpectedVersion'."
        }
    }
    if ($globalResult.StandardOutput -cne $localResult.StandardOutput -or
        $globalResult.StandardOutput -cne $dnxResult.StandardOutput) {
        throw "Global, local, and dnx version output documents disagree."
    }
    $unexpectedDnxExecutable = [System.IO.Path]::Combine(
        $dnxCliHome,
        ".dotnet",
        "tools",
        $executableName)
    if (Test-Path -LiteralPath $unexpectedDnxExecutable) {
        throw "dnx created a persistent global command."
    }

    $sdkResult = Invoke-Captured -FileName $dotnet -Arguments @("--version")
    Assert-Success -Result $sdkResult -Operation "SDK version query"
    $sdkVersion = $sdkResult.StandardOutput.Trim()
    if ([string]::IsNullOrWhiteSpace($sdkVersion) -or
        $sdkVersion.Contains("`n")) {
        throw "SDK version query returned an unexpected result."
    }

    $packageNames = @(
        $package.Name,
        [System.IO.Path]::GetFileName($symbolPackagePath)
    )
    [System.Array]::Sort($packageNames, [System.StringComparer]::Ordinal)
    $packageFiles = @(
        foreach ($name in $packageNames) {
            $path = [System.IO.Path]::Combine($resolvedPackageDirectory, $name)
            [ordered]@{
                name = $name
                sha256 = (Get-FileHash `
                    -LiteralPath $path `
                    -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    )
    $evidence = [ordered]@{
        schema = "dotnet-axi/release-candidate-platform-evidence/v1"
        candidate_commit = $ExpectedCommit
        requested_version = $ExpectedVersion
        observed_package_id = $packageId
        observed_package_version = $packageVersion
        observed_symbol_package_id = [string] $symbolMetadata.id
        observed_symbol_package_version = [string] $symbolMetadata.version
        observed_symbol_repository_commit =
            [string] $symbolMetadata.repository.commit
        observed_symbol_package_type =
            [string] $symbolMetadata.packageTypes.packageType.name
        observed_versions = [ordered]@{
            global = $globalVersion
            local = $localVersion
            dnx = $dnxVersion
        }
        runner_os = $RunnerOs
        sdk_version = $sdkVersion
        os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
        package_files = $packageFiles
    }
    $evidenceDirectory = [System.IO.Path]::GetDirectoryName(
        [System.IO.Path]::GetFullPath($EvidencePath))
    [System.IO.Directory]::CreateDirectory($evidenceDirectory) | Out-Null
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::GetFullPath($EvidencePath),
        (($evidence | ConvertTo-Json -Depth 6) + "`n"),
        [System.Text.UTF8Encoding]::new($false))
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host (
    "Verified dnx-first, global, and local version parity for dnaxi " +
    "$ExpectedVersion on $RunnerOs.")
