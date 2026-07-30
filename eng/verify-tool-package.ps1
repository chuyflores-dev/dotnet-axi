[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchive] $Archive,

        [Parameter(Mandatory)]
        [string] $EntryName
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "Package entry '$EntryName' is missing."
    }

    $stream = $entry.Open()
    $reader = [System.IO.StreamReader]::new(
        $stream,
        [System.Text.UTF8Encoding]::new($false, $true),
        $true)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Assert-PortablePdb {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchive] $Archive,

        [Parameter(Mandatory)]
        [string] $EntryName
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "Symbol package entry '$EntryName' is missing."
    }

    $stream = $entry.Open()
    try {
        [byte[]] $magicBytes = 0, 0, 0, 0
        if ($stream.Read($magicBytes, 0, $magicBytes.Length) -ne 4) {
            throw "Symbol package entry '$EntryName' is truncated."
        }

        $magic = [System.Text.Encoding]::ASCII.GetString($magicBytes)
        if ($magic -ne "BSJB") {
            throw "Symbol package entry '$EntryName' is not a portable PDB."
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Invoke-Captured {
    param(
        [Parameter(Mandatory)]
        [string] $FileName,

        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [hashtable] $Environment = @{}
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    $startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
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
        $process.WaitForExit()
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

function Assert-VersionOutput {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Result,

        [Parameter(Mandatory)]
        [string] $Version,

        [bool] $RequireEmptyStandardError = $true
    )

    if ($Result.ExitCode -ne 0) {
        throw "Version invocation exited $($Result.ExitCode). stderr: $($Result.StandardError)"
    }

    if ($RequireEmptyStandardError -and $Result.StandardError.Length -ne 0) {
        throw "Version invocation wrote unexpected stderr: $($Result.StandardError)"
    }

    if ($Result.StandardOutput.Contains("`r")) {
        throw "Version output contains a carriage return instead of LF-only output."
    }

    $lines = $Result.StandardOutput -split "`n"
    $requiredLines = @(
        "schema: dotnet-axi/v1",
        "command: version",
        "status: success",
        "tool: dotnet-axi",
        "tool_version: $Version",
        "output_schema: dotnet-axi/v1"
    )
    foreach ($line in $requiredLines) {
        if ($lines -notcontains $line) {
            throw "Version output is missing '$line'. Output: $($Result.StandardOutput)"
        }
    }
}

$resolvedPackageDirectory = (
    Resolve-Path -LiteralPath $PackageDirectory
).Path
$packages = @(
    Get-ChildItem -LiteralPath $resolvedPackageDirectory -File |
        Where-Object {
            $_.Name -like "dotnet-axi.*.nupkg" -and
            $_.Name -notlike "*.snupkg"
        }
)
if ($packages.Count -ne 1) {
    throw "Expected one dotnet-axi .nupkg in '$resolvedPackageDirectory'; found $($packages.Count)."
}

$package = $packages[0]
$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $nuspecEntries = @(
        $archive.Entries |
            Where-Object { $_.FullName -like "*.nuspec" }
    )
    if ($nuspecEntries.Count -ne 1) {
        throw "Expected one nuspec entry; found $($nuspecEntries.Count)."
    }

    [xml] $nuspec = Read-ZipEntryText `
        -Archive $archive `
        -EntryName $nuspecEntries[0].FullName
    $metadata = $nuspec.package.metadata
    $version = [string] $metadata.version

    if ([string] $metadata.id -ne "dotnet-axi") {
        throw "Package ID is '$($metadata.id)', expected 'dotnet-axi'."
    }
    if ([string] $metadata.license.InnerText -ne "Apache-2.0" -or
        [string] $metadata.license.type -ne "expression") {
        throw "Package license must be the Apache-2.0 expression."
    }
    if ([string] $metadata.readme -ne "README.md") {
        throw "Package readme metadata must point to README.md."
    }
    if ([string] $metadata.packageTypes.packageType.name -ne "DotnetTool") {
        throw "Package type must be DotnetTool."
    }
    if ([string] $metadata.repository.type -ne "git" -or
        [string] $metadata.repository.url -ne
            "https://github.com/chuyflores-dev/dotnet-axi.git" -or
        [string]::IsNullOrWhiteSpace(
            [string] $metadata.repository.commit)) {
        throw "Package repository metadata is incomplete."
    }

    $assemblyNames = @(
        "DotNetAxi.Analysis",
        "DotNetAxi.Axi",
        "DotNetAxi.Changes",
        "DotNetAxi.Contracts",
        "DotNetAxi.DotNet",
        "DotNetAxi.Graph",
        "DotNetAxi.Roslyn",
        "DotNetAxi.Search",
        "DotNetAxi.Structural",
        "DotNetAxi.Validation",
        "DotNetAxi.Workspaces",
        "dnaxi"
    )
    $requiredPackageEntries = @(
        "README.md",
        "tools/net10.0/any/DotnetToolSettings.xml",
        "tools/net10.0/any/dnaxi.deps.json",
        "tools/net10.0/any/dnaxi.runtimeconfig.json",
        "tools/net10.0/any/System.CommandLine.dll"
    ) + @(
        $assemblyNames |
            ForEach-Object { "tools/net10.0/any/$_.dll" }
    )
    foreach ($entryName in $requiredPackageEntries) {
        if ($null -eq $archive.GetEntry($entryName)) {
            throw "Package entry '$entryName' is missing."
        }
    }

    $dependencyModel = Read-ZipEntryText `
        -Archive $archive `
        -EntryName "tools/net10.0/any/dnaxi.deps.json" |
            ConvertFrom-Json
    if ($dependencyModel.libraries.PSObject.Properties.Name -notcontains
        "System.CommandLine/2.0.10") {
        throw "The packaged System.CommandLine dependency is not pinned to 2.0.10."
    }

    [xml] $toolSettings = Read-ZipEntryText `
        -Archive $archive `
        -EntryName "tools/net10.0/any/DotnetToolSettings.xml"
    $command = $toolSettings.DotNetCliTool.Commands.Command
    if ([string] $command.Name -ne "dnaxi" -or
        [string] $command.EntryPoint -ne "dnaxi.dll" -or
        [string] $command.Runner -ne "dotnet") {
        throw "DotnetToolSettings.xml does not declare the dnaxi entry point."
    }
}
finally {
    $archive.Dispose()
}

$symbolPackagePath = [System.IO.Path]::ChangeExtension(
    $package.FullName,
    ".snupkg")
if (-not (Test-Path -LiteralPath $symbolPackagePath -PathType Leaf)) {
    throw "Symbol package '$symbolPackagePath' is missing."
}

$symbolArchive = [System.IO.Compression.ZipFile]::OpenRead(
    $symbolPackagePath)
try {
    foreach ($assemblyName in $assemblyNames) {
        Assert-PortablePdb `
            -Archive $symbolArchive `
            -EntryName "tools/net10.0/any/$assemblyName.pdb"
    }
}
finally {
    $symbolArchive.Dispose()
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$temporaryToolPath = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(),
    "dnaxi-package-" + [System.Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($temporaryToolPath) | Out-Null
try {
    $isolatedEnvironment = @{
        DOTNET_CLI_HOME = [System.IO.Path]::Combine(
            $temporaryToolPath,
            "dotnet-home")
        NUGET_PACKAGES = [System.IO.Path]::Combine(
            $temporaryToolPath,
            "install-packages")
    }
    $install = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool",
            "install",
            "dotnet-axi",
            "--tool-path",
            $temporaryToolPath,
            "--version",
            $version,
            "--source",
            $resolvedPackageDirectory,
            "--no-cache",
            "--verbosity",
            "quiet"
        ) `
        -Environment $isolatedEnvironment
    if ($install.ExitCode -ne 0) {
        throw "Local tool installation failed. stderr: $($install.StandardError)"
    }

    $executableName = if ([System.OperatingSystem]::IsWindows()) {
        "dnaxi.exe"
    }
    else {
        "dnaxi"
    }
    $executable = [System.IO.Path]::Combine(
        $temporaryToolPath,
        $executableName)
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Installed command '$executable' is missing."
    }

    $installedVersion = Invoke-Captured `
        -FileName $executable `
        -Arguments @("--version")
    Assert-VersionOutput `
        -Result $installedVersion `
        -Version $version

    $dnx = (Get-Command dnx -ErrorAction Stop).Source
    $oneShotVersion = Invoke-Captured `
        -FileName $dnx `
        -Arguments @(
            "dotnet-axi@$version",
            "--source",
            $resolvedPackageDirectory,
            "--no-cache",
            "--",
            "--version"
        ) `
        -Environment @{
            DOTNET_CLI_HOME = $isolatedEnvironment.DOTNET_CLI_HOME
            NUGET_PACKAGES = [System.IO.Path]::Combine(
                $temporaryToolPath,
                "dnx-packages")
        }
    Assert-VersionOutput `
        -Result $oneShotVersion `
        -Version $version `
        -RequireEmptyStandardError $false
}
finally {
    if (Test-Path -LiteralPath $temporaryToolPath) {
        Remove-Item -LiteralPath $temporaryToolPath -Recurse -Force
    }
}

Write-Host "Verified dotnet-axi $version package, symbols, isolated install, and dnx invocation."
