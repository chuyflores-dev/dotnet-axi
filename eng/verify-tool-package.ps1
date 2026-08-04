[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [string] $ExpectedVersion,

    [string] $ExpectedCommit
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

function Read-ZipEntryBytes {
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
    $buffer = [System.IO.MemoryStream]::new()
    try {
        $stream.CopyTo($buffer)
        return ,$buffer.ToArray()
    }
    finally {
        $buffer.Dispose()
        $stream.Dispose()
    }
}

function Assert-ZipEntryMatchesFile {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchive] $Archive,

        [Parameter(Mandatory)]
        [string] $EntryName,

        [Parameter(Mandatory)]
        [string] $FilePath
    )

    [byte[]] $expected = [System.IO.File]::ReadAllBytes($FilePath)
    [byte[]] $actual = Read-ZipEntryBytes `
        -Archive $Archive `
        -EntryName $EntryName
    if ($expected.Length -ne $actual.Length) {
        throw "Package entry '$EntryName' is not byte-identical to '$FilePath'."
    }

    for ($index = 0; $index -lt $expected.Length; $index++) {
        if ($expected[$index] -ne $actual[$index]) {
            throw "Package entry '$EntryName' is not byte-identical to '$FilePath'."
        }
    }
}

function Install-AgentSkillFromPackage {
    param(
        [Parameter(Mandatory)]
        [string] $PackagePath,

        [Parameter(Mandatory)]
        [string] $ScopeRoot
    )

    $installation = [System.IO.Path]::Combine(
        $ScopeRoot,
        ".agents",
        "skills",
        "dotnet-axi")
    [System.IO.Directory]::CreateDirectory($installation) | Out-Null
    $entries = @{
        "skills/dotnet-axi/SKILL.md" = "SKILL.md"
        "skills/dotnet-axi/references/codex.md" =
            [System.IO.Path]::Combine("references", "codex.md")
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        foreach ($entryName in $entries.Keys) {
            $destination = [System.IO.Path]::Combine(
                $installation,
                $entries[$entryName])
            [System.IO.Directory]::CreateDirectory(
                [System.IO.Path]::GetDirectoryName($destination)) |
                Out-Null
            [System.IO.File]::WriteAllBytes(
                $destination,
                (Read-ZipEntryBytes `
                    -Archive $archive `
                    -EntryName $entryName))
        }
    }
    finally {
        $archive.Dispose()
    }

    return $installation
}

function Assert-InstalledAgentSkill {
    param(
        [Parameter(Mandatory)]
        [string] $Installation
    )

    $skillPath = [System.IO.Path]::Combine($Installation, "SKILL.md")
    $codexPath = [System.IO.Path]::Combine(
        $Installation,
        "references",
        "codex.md")
    if (-not (Test-Path -LiteralPath $skillPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $codexPath -PathType Leaf)) {
        throw "Installed Agent Skill is incomplete at '$Installation'."
    }

    $skill = [System.IO.File]::ReadAllText($skillPath)
    if (-not $skill.StartsWith(
            "---`nname: dotnet-axi`ndescription: ",
            [System.StringComparison]::Ordinal) -or
        -not $skill.Contains("`n---`n")) {
        throw "Installed Agent Skill metadata is not portable or discoverable."
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

function Assert-AssemblyVersionMetadata {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchive] $Archive,

        [Parameter(Mandatory)]
        [string] $Version
    )

    [byte[]] $assemblyBytes = Read-ZipEntryBytes `
        -Archive $Archive `
        -EntryName "tools/net10.0/any/dnaxi.dll"
    $stream = [System.IO.MemoryStream]::new($assemblyBytes, $false)
    $peReader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $metadataReader = (
            [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader(
                $peReader)
        )
        $assembly = $metadataReader.GetAssemblyDefinition()
        $informationalVersions = @()
        $toolVersions = @()

        foreach ($handle in $assembly.GetCustomAttributes()) {
            $attribute = $metadataReader.GetCustomAttribute($handle)
            if ($attribute.Constructor.Kind -ne
                [System.Reflection.Metadata.HandleKind]::MemberReference) {
                continue
            }

            $constructor = [System.Reflection.Metadata.MemberReferenceHandle] `
                $attribute.Constructor
            $member = $metadataReader.GetMemberReference($constructor)
            if ($member.Parent.Kind -ne
                [System.Reflection.Metadata.HandleKind]::TypeReference) {
                continue
            }

            $type = $metadataReader.GetTypeReference(
                [System.Reflection.Metadata.TypeReferenceHandle] $member.Parent)
            $attributeName = $metadataReader.GetString($type.Name)
            if ($attributeName -notin @(
                    "AssemblyInformationalVersionAttribute",
                    "AssemblyMetadataAttribute")) {
                continue
            }

            $blob = $metadataReader.GetBlobReader($attribute.Value)
            if ($blob.ReadUInt16() -ne 1) {
                throw "Assembly attribute '$attributeName' has an invalid prolog."
            }

            $first = $blob.ReadSerializedString()
            if ($attributeName -eq "AssemblyInformationalVersionAttribute") {
                $informationalVersions += $first
                continue
            }

            $second = $blob.ReadSerializedString()
            if ($first -ceq "DotNetAxi.ToolVersion") {
                $toolVersions += $second
            }
        }

        if ($informationalVersions.Count -ne 1 -or
            $informationalVersions[0] -cne $Version) {
            throw (
                "Packaged assembly informational version is " +
                "'$($informationalVersions -join ',')', expected '$Version'.")
        }
        if ($toolVersions.Count -ne 1 -or $toolVersions[0] -cne $Version) {
            throw (
                "Packaged assembly tool version is " +
                "'$($toolVersions -join ',')', expected '$Version'.")
        }
    }
    finally {
        $peReader.Dispose()
        $stream.Dispose()
    }
}

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
    $extensions = @("")
    if ([System.OperatingSystem]::IsWindows() -and
        [string]::IsNullOrEmpty([System.IO.Path]::GetExtension($Command))) {
        $pathExtensions = [System.Environment]::GetEnvironmentVariable(
            "PATHEXT")
        $extensions += $pathExtensions -split ";"
    }

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
        throw "$Operation exited $($Result.ExitCode). stderr: $($Result.StandardError)"
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

function Assert-HelpOutput {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Result,

        [bool] $RequireEmptyStandardError = $true
    )

    Assert-Success -Result $Result -Operation "Help invocation"

    if ($RequireEmptyStandardError -and $Result.StandardError.Length -ne 0) {
        throw "Help invocation wrote unexpected stderr: $($Result.StandardError)"
    }

    if ($Result.StandardOutput.Contains("`r")) {
        throw "Help output contains a carriage return instead of LF-only output."
    }

    $lines = $Result.StandardOutput -split "`n"
    $requiredLines = @(
        "schema: dotnet-axi/v1",
        "command: help",
        "status: success",
        "topic: home",
        "classification: passive",
        "arguments: []"
    )
    foreach ($line in $requiredLines) {
        if ($lines -notcontains $line) {
            throw "Help output is missing '$line'. Output: $($Result.StandardOutput)"
        }
    }

    if (-not $Result.StandardOutput.Contains("dnaxi --version")) {
        throw "Help output does not contain the registered version example."
    }

    if (-not $Result.StandardOutput.Contains("guidance:") -or
        -not $Result.StandardOutput.Contains(
            "invocation: dnx dotnet-axi -- <command>") -or
        -not $Result.StandardOutput.Contains(
            "Treat the invoked version's structured help")) {
        throw "Help output does not contain canonical Agent Skill guidance."
    }
}

function Assert-HomeOutput {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Result
    )

    Assert-Success -Result $Result -Operation "Home invocation"
    $required = @(
        "schema: dotnet-axi/v1",
        "command: home",
        "status: success",
        "guidance:",
        "invocation: dnx dotnet-axi -- <command>",
        "Do not claim completion solely because files changed."
    )
    foreach ($text in $required) {
        if (-not $Result.StandardOutput.Contains($text)) {
            throw "Home output is missing '$text'. Output: $($Result.StandardOutput)"
        }
    }
}

function Assert-SameOutput {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Expected,

        [Parameter(Mandatory)]
        [pscustomobject] $Actual,

        [Parameter(Mandatory)]
        [string] $Comparison
    )

    if ($Expected.StandardOutput -cne $Actual.StandardOutput) {
        throw (
            "$Comparison produced different stdout documents. " +
            "Expected ($($Expected.StandardOutput.Length)): " +
            "$($Expected.StandardOutput) Actual " +
            "($($Actual.StandardOutput.Length)): $($Actual.StandardOutput)"
        )
    }
}

$resolvedPackageDirectory = (
    Resolve-Path -LiteralPath $PackageDirectory
).Path
$repositoryRoot = (
    Resolve-Path -LiteralPath (
        [System.IO.Path]::Combine($PSScriptRoot, ".."))
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

    if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and
        $version -cne $ExpectedVersion) {
        throw "Package version is '$version', expected '$ExpectedVersion'."
    }

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
    if (-not [string]::IsNullOrWhiteSpace($ExpectedCommit) -and
        [string] $metadata.repository.commit -cne $ExpectedCommit) {
        throw (
            "Package repository commit is " +
            "'$([string] $metadata.repository.commit)', expected " +
            "'$ExpectedCommit'.")
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
        "skills/dotnet-axi/SKILL.md",
        "skills/dotnet-axi/references/codex.md",
        "tools/net10.0/any/DotnetToolSettings.xml",
        "tools/net10.0/any/dnaxi.deps.json",
        "tools/net10.0/any/dnaxi.runtimeconfig.json",
        "tools/net10.0/any/Microsoft.Build.Locator.dll",
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

    Assert-AssemblyVersionMetadata `
        -Archive $archive `
        -Version $version

    Assert-ZipEntryMatchesFile `
        -Archive $archive `
        -EntryName "skills/dotnet-axi/SKILL.md" `
        -FilePath ([System.IO.Path]::Combine(
            $repositoryRoot,
            "skills",
            "dotnet-axi",
            "SKILL.md"))
    Assert-ZipEntryMatchesFile `
        -Archive $archive `
        -EntryName "skills/dotnet-axi/references/codex.md" `
        -FilePath ([System.IO.Path]::Combine(
            $repositoryRoot,
            "skills",
            "dotnet-axi",
            "references",
            "codex.md"))

    $dependencyModel = Read-ZipEntryText `
        -Archive $archive `
        -EntryName "tools/net10.0/any/dnaxi.deps.json" |
            ConvertFrom-Json
    if ($dependencyModel.libraries.PSObject.Properties.Name -notcontains
        "System.CommandLine/2.0.10") {
        throw "The packaged System.CommandLine dependency is not pinned to 2.0.10."
    }
    if ($dependencyModel.libraries.PSObject.Properties.Name -notcontains
        "Microsoft.Build.Locator/1.11.2") {
        throw "The packaged Microsoft.Build.Locator dependency is not pinned to 1.11.2."
    }
    $msBuildRuntimeLibraries = @(
        $dependencyModel.libraries.PSObject.Properties.Name |
            Where-Object { $_ -like "Microsoft.Build/*" }
    )
    if ($msBuildRuntimeLibraries.Count -ne 0) {
        throw "The package must load Microsoft.Build from the selected SDK, not ship it as a runtime dependency."
    }
    $msBuildRuntimeEntries = @(
        $archive.Entries |
            Where-Object {
                [System.IO.Path]::GetFileName($_.FullName) -eq
                    "Microsoft.Build.dll"
            }
    )
    if ($msBuildRuntimeEntries.Count -ne 0) {
        throw "The package must not contain a Microsoft.Build runtime assembly."
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
$dnx = (Get-Command dnx -ErrorAction Stop).Source
$temporaryRoot = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(),
    "dnaxi-package-" + [System.Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
try {
    $repositorySkill = Install-AgentSkillFromPackage `
        -PackagePath $package.FullName `
        -ScopeRoot ([System.IO.Path]::Combine(
            $temporaryRoot,
            "repository-scope"))
    $userSkill = Install-AgentSkillFromPackage `
        -PackagePath $package.FullName `
        -ScopeRoot ([System.IO.Path]::Combine(
            $temporaryRoot,
            "user-scope"))
    Assert-InstalledAgentSkill -Installation $repositorySkill
    Assert-InstalledAgentSkill -Installation $userSkill

    $globalCliHome = [System.IO.Path]::Combine(
        $temporaryRoot,
        "global-home")
    $globalEnvironment = @{
        DOTNET_CLI_HOME = $globalCliHome
        NUGET_PACKAGES = [System.IO.Path]::Combine(
            $temporaryRoot,
            "global-packages")
    }
    $globalInstall = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool",
            "install",
            "--global",
            "dotnet-axi",
            "--version",
            $version,
            "--source",
            $resolvedPackageDirectory,
            "--no-http-cache",
            "--verbosity",
            "quiet"
        ) `
        -Environment $globalEnvironment
    Assert-Success `
        -Result $globalInstall `
        -Operation "Global tool installation"

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
        [System.Environment]::GetEnvironmentVariable("PATH")
    )
    $globalVersion = Invoke-Captured `
        -FileName "dnaxi" `
        -Arguments @("--version") `
        -Environment $globalEnvironment
    Assert-VersionOutput `
        -Result $globalVersion `
        -Version $version

    $globalHelp = Invoke-Captured `
        -FileName "dnaxi" `
        -Arguments @("--help") `
        -Environment $globalEnvironment
    Assert-HelpOutput -Result $globalHelp

    $globalUpdate = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool",
            "update",
            "--global",
            "dotnet-axi",
            "--version",
            $version,
            "--source",
            $resolvedPackageDirectory,
            "--no-http-cache",
            "--verbosity",
            "quiet"
        ) `
        -Environment $globalEnvironment
    Assert-Success `
        -Result $globalUpdate `
        -Operation "Global tool update"

    $updatedGlobalVersion = Invoke-Captured `
        -FileName "dnaxi" `
        -Arguments @("--version") `
        -Environment $globalEnvironment
    Assert-VersionOutput `
        -Result $updatedGlobalVersion `
        -Version $version
    Assert-SameOutput `
        -Expected $globalVersion `
        -Actual $updatedGlobalVersion `
        -Comparison "Global update"

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
    }
    $manifestCreation = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "new",
            "tool-manifest",
            "--no-update-check"
        ) `
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
            "tool",
            "install",
            "--local",
            "dotnet-axi",
            "--tool-manifest",
            $manifestPath,
            "--version",
            $version,
            "--source",
            $resolvedPackageDirectory,
            "--no-http-cache",
            "--verbosity",
            "quiet"
        ) `
        -Environment $localEnvironment `
        -WorkingDirectory $localWorkspace
    Assert-Success `
        -Result $localInstall `
        -Operation "Local tool installation"

    $localVersion = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool",
            "run",
            "dnaxi",
            "--",
            "--version"
        ) `
        -Environment $localEnvironment `
        -WorkingDirectory $localWorkspace
    Assert-VersionOutput `
        -Result $localVersion `
        -Version $version

    $localHelp = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool",
            "run",
            "dnaxi",
            "--",
            "--help"
        ) `
        -Environment $localEnvironment `
        -WorkingDirectory $localWorkspace
    Assert-HelpOutput -Result $localHelp

    $localUpdate = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool",
            "update",
            "--local",
            "dotnet-axi",
            "--tool-manifest",
            $manifestPath,
            "--version",
            $version,
            "--source",
            $resolvedPackageDirectory,
            "--no-http-cache",
            "--verbosity",
            "quiet"
        ) `
        -Environment $localEnvironment `
        -WorkingDirectory $localWorkspace
    Assert-Success `
        -Result $localUpdate `
        -Operation "Local tool update"

    $updatedLocalVersion = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool",
            "run",
            "dnaxi",
            "--",
            "--version"
        ) `
        -Environment $localEnvironment `
        -WorkingDirectory $localWorkspace
    Assert-VersionOutput `
        -Result $updatedLocalVersion `
        -Version $version
    Assert-SameOutput `
        -Expected $localVersion `
        -Actual $updatedLocalVersion `
        -Comparison "Local update"

    $dnxCliHome = [System.IO.Path]::Combine(
        $temporaryRoot,
        "dnx-home")
    $dnxEnvironment = @{
        DOTNET_CLI_HOME = $dnxCliHome
        NUGET_PACKAGES = [System.IO.Path]::Combine(
            $temporaryRoot,
            "dnx-packages")
    }
    $oneShotVersion = Invoke-Captured `
        -FileName $dnx `
        -Arguments @(
            "dotnet-axi@$version",
            "--source",
            $resolvedPackageDirectory,
            "--no-http-cache",
            "--verbosity",
            "quiet",
            "--",
            "--version"
        ) `
        -Environment $dnxEnvironment
    Assert-VersionOutput `
        -Result $oneShotVersion `
        -Version $version `
        -RequireEmptyStandardError $false

    $oneShotHelp = Invoke-Captured `
        -FileName $dnx `
        -Arguments @(
            "dotnet-axi@$version",
            "--source",
            $resolvedPackageDirectory,
            "--no-http-cache",
            "--verbosity",
            "quiet",
            "--",
            "--help"
        ) `
        -Environment $dnxEnvironment
    Assert-HelpOutput `
        -Result $oneShotHelp `
        -RequireEmptyStandardError $false

    $oneShotWorkspace = [System.IO.Path]::Combine(
        $temporaryRoot,
        "one-shot-workspace")
    [System.IO.Directory]::CreateDirectory($oneShotWorkspace) | Out-Null
    $oneShotHome = Invoke-Captured `
        -FileName $dnx `
        -Arguments @(
            "dotnet-axi@$version",
            "--source",
            $resolvedPackageDirectory,
            "--no-http-cache",
            "--verbosity",
            "quiet",
            "--"
        ) `
        -Environment $dnxEnvironment `
        -WorkingDirectory $oneShotWorkspace
    Assert-HomeOutput -Result $oneShotHome

    Assert-SameOutput `
        -Expected $globalVersion `
        -Actual $localVersion `
        -Comparison "Global and local version invocation"
    Assert-SameOutput `
        -Expected $globalVersion `
        -Actual $oneShotVersion `
        -Comparison "Global and dnx version invocation"
    Assert-SameOutput `
        -Expected $globalHelp `
        -Actual $localHelp `
        -Comparison "Global and local help invocation"
    Assert-SameOutput `
        -Expected $globalHelp `
        -Actual $oneShotHelp `
        -Comparison "Global and dnx help invocation"

    $unexpectedDnxExecutable = [System.IO.Path]::Combine(
        $dnxCliHome,
        ".dotnet",
        "tools",
        $executableName)
    if (Test-Path -LiteralPath $unexpectedDnxExecutable) {
        throw "One-shot dnx invocation created a persistent global command."
    }

    $localUninstall = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool",
            "uninstall",
            "--local",
            "dotnet-axi",
            "--tool-manifest",
            $manifestPath
        ) `
        -Environment $localEnvironment `
        -WorkingDirectory $localWorkspace
    Assert-Success `
        -Result $localUninstall `
        -Operation "Local tool uninstall"

    $removedLocalInvocation = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool",
            "run",
            "dnaxi",
            "--",
            "--version"
        ) `
        -Environment $localEnvironment `
        -WorkingDirectory $localWorkspace
    if ($removedLocalInvocation.ExitCode -eq 0) {
        throw "Local tool invocation succeeded after uninstall."
    }

    $globalUninstall = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool",
            "uninstall",
            "--global",
            "dotnet-axi"
        ) `
        -Environment $globalEnvironment
    Assert-Success `
        -Result $globalUninstall `
        -Operation "Global tool uninstall"

    if (Test-Path -LiteralPath $globalExecutable) {
        throw "Global command '$globalExecutable' remains after uninstall."
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host "Verified dotnet-axi $version package, symbols, Agent Skill, global/local lifecycle, and dnx parity."
