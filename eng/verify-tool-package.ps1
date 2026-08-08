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
    if (-not $Result.StandardOutput.StartsWith(
            "schema: dotnet-axi/v1`n",
            [System.StringComparison]::Ordinal)) {
        throw "Version stdout is contaminated before the structured document."
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

    $requiredCapabilityFragments = @(
        "capabilities:`n  selected_host:`n",
        "`n  sdk:`n",
        "`n  ms_build:`n",
        "`n  roslyn:`n",
        "`n  git:`n",
        "`n  optional_engines[1]",
        "`n  command_engines[1]{command,preferred_engine,selected_engine,degradation}:`n"
    )
    foreach ($fragment in $requiredCapabilityFragments) {
        if (-not $Result.StandardOutput.Contains(
                $fragment,
                [System.StringComparison]::Ordinal)) {
            throw "Version output is missing capability fragment '$fragment'. Output: $($Result.StandardOutput)"
        }
    }
}

function Assert-HelpOutput {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Result,

        [Parameter(Mandatory)]
        [string] $Version,

        [bool] $RequireEmptyStandardError = $true
    )

    Assert-Success -Result $Result -Operation "Help invocation"

    if ($RequireEmptyStandardError -and $Result.StandardError.Length -ne 0) {
        throw "Help invocation wrote unexpected stderr: $($Result.StandardError)"
    }

    if ($Result.StandardOutput.Contains("`r")) {
        throw "Help output contains a carriage return instead of LF-only output."
    }
    if (-not $Result.StandardOutput.StartsWith(
            "schema: dotnet-axi/v1`n",
            [System.StringComparison]::Ordinal)) {
        throw "Help stdout is contaminated before the structured document."
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

    if (-not $Result.StandardOutput.Contains(
            "dnx dnaxi@$Version --verbosity quiet -- --version")) {
        throw "Help output does not contain the registered version example."
    }

    if (-not $Result.StandardOutput.Contains("guidance:") -or
        -not $Result.StandardOutput.Contains(
            "invocation: dnx dnaxi@$Version --verbosity quiet -- <command>") -or
        -not $Result.StandardOutput.Contains(
            "Treat the invoked version's structured help") -or
        -not $Result.StandardOutput.Contains("next_steps[3]:") -or
        -not $Result.StandardOutput.Contains(
            "Invoke known source-discovery routes directly") -or
        -not $Result.StandardOutput.Contains(
            "Inspect only the narrowest relevant help once when no documented route or option applies")) {
        throw "Help output does not contain compact activation guidance."
    }

    foreach ($redundantHelpProbe in @(
            "search file --help",
            "search text --help",
            "search syntax --help",
            "search syntax invocation --help")) {
        if ($Result.StandardOutput.Contains($redundantHelpProbe)) {
            throw "Help output contains redundant probe '$redundantHelpProbe'."
        }
    }

    foreach ($omitted in @(
            "source_discovery_flow:",
            "invocation_flow:",
            "safety_flow:",
            "completion:")) {
        if ($Result.StandardOutput.Contains($omitted)) {
            throw "Help output embeds full Agent Skill field '$omitted'."
        }
    }
}

function Assert-HomeOutput {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Result,

        [Parameter(Mandatory)]
        [string] $Version
    )

    Assert-Success -Result $Result -Operation "Home invocation"
    if (-not $Result.StandardOutput.StartsWith(
            "schema: dotnet-axi/v1`n",
            [System.StringComparison]::Ordinal)) {
        throw "Home stdout is contaminated before the structured document."
    }
    $required = @(
        "schema: dotnet-axi/v1",
        "command: home",
        "status: success",
        "tool: dotnet-axi",
        "output_schema: dotnet-axi/v1",
        "capabilities:`n  selected_host:`n",
        "command_engines[1]{command,preferred_engine,selected_engine,degradation}",
        "guidance:",
        "invocation: dnx dnaxi@$Version --verbosity quiet -- <command>",
        "next_steps[3]:",
        "Invoke known source-discovery routes directly",
        "Inspect only the narrowest relevant help once when no documented route or option applies",
        "Read an already-known file directly when that is smaller.",
        "suggestions[1]:`n  - command: dnx`n    arguments[5]: dnaxi@$Version,`"--verbosity`",quiet,`"--`",`"--help`"",
        "do not add a help probe before a known route."
    )
    foreach ($text in $required) {
        if (-not $Result.StandardOutput.Contains($text)) {
            throw "Home output is missing '$text'. Output: $($Result.StandardOutput)"
        }
    }

    foreach ($redundantHelpProbe in @(
            "search file --help",
            "search text --help",
            "search syntax --help",
            "search syntax invocation --help")) {
        if ($Result.StandardOutput.Contains($redundantHelpProbe)) {
            throw "Home output contains redundant probe '$redundantHelpProbe'."
        }
    }
}

function Assert-PassiveCommandOutput {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Result,

        [Parameter(Mandatory)]
        [string] $Command
    )

    Assert-Success -Result $Result -Operation "$Command dnx invocation"
    if (-not $Result.StandardOutput.StartsWith(
            "schema: dotnet-axi/v1`n",
            [System.StringComparison]::Ordinal)) {
        throw "$Command stdout is contaminated before the structured document."
    }
    if ($Result.StandardOutput.Contains("`r")) {
        throw "$Command output contains a carriage return instead of LF-only output."
    }

    $lines = $Result.StandardOutput -split "`n"
    foreach ($line in @(
            "schema: dotnet-axi/v1",
            "command: $Command",
            "status: success")) {
        if ($lines -notcontains $line) {
            throw "$Command output is missing '$line'. Output: $($Result.StandardOutput)"
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

function New-OptionalDependencyShim {
    param(
        [Parameter(Mandatory)]
        [string] $Directory,

        [Parameter(Mandatory)]
        [ValidateSet("git", "rg")]
        [string] $Command,

        [Parameter(Mandatory)]
        [string] $ApplicationDirectory,

        [Parameter(Mandatory)]
        [string] $Marker,

        [AllowNull()]
        [string] $Version,

        [Parameter(Mandatory)]
        [int] $ExitCode
    )

    [System.IO.Directory]::CreateDirectory($Directory) | Out-Null
    $applicationName = "DotNetAxi.DotNet.ProcessTestApp"
    $applicationFileName = if ([System.OperatingSystem]::IsWindows()) {
        "$applicationName.exe"
    }
    else {
        $applicationName
    }
    $applicationPath = [System.IO.Path]::Combine(
        $ApplicationDirectory,
        $applicationFileName)
    if (-not [System.IO.File]::Exists($applicationPath)) {
        throw "Optional-dependency shim application '$applicationPath' is missing."
    }

    foreach ($extension in @(".dll", ".deps.json", ".runtimeconfig.json")) {
        $source = [System.IO.Path]::Combine(
            $ApplicationDirectory,
            "$applicationName$extension")
        if (-not [System.IO.File]::Exists($source)) {
            throw "Optional-dependency shim asset '$source' is missing."
        }
        [System.IO.File]::Copy(
            $source,
            [System.IO.Path]::Combine($Directory, "$applicationName$extension"),
            $true)
    }

    $fileName = if ([System.OperatingSystem]::IsWindows()) {
        "$Command.exe"
    }
    else {
        $Command
    }
    $path = [System.IO.Path]::Combine($Directory, $fileName)
    [System.IO.File]::Copy($applicationPath, $path, $true)
    if (-not [System.OperatingSystem]::IsWindows()) {
        $mode = [System.IO.UnixFileMode]::UserRead -bor
            [System.IO.UnixFileMode]::UserWrite -bor
            [System.IO.UnixFileMode]::UserExecute -bor
            [System.IO.UnixFileMode]::GroupRead -bor
            [System.IO.UnixFileMode]::GroupExecute -bor
            [System.IO.UnixFileMode]::OtherRead -bor
            [System.IO.UnixFileMode]::OtherExecute
        [System.IO.File]::SetUnixFileMode($path, $mode)
    }

    [System.IO.File]::WriteAllText(
        [System.IO.Path]::Combine($Directory, "$Command.marker-path"),
        $Marker)
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::Combine($Directory, "$Command.exit-code"),
        [string] $ExitCode)
    $versionPath = [System.IO.Path]::Combine(
        $Directory,
        "$Command.version")
    if ([string]::IsNullOrWhiteSpace($Version)) {
        [System.IO.File]::Delete($versionPath)
    }
    else {
        [System.IO.File]::WriteAllText($versionPath, $Version)
    }

    return $path
}

function Assert-OptionalDependencyScenarios {
    param(
        [Parameter(Mandatory)]
        [string] $Executable,

        [Parameter(Mandatory)]
        [hashtable] $BaseEnvironment,

        [Parameter(Mandatory)]
        [string] $TemporaryRoot,

        [Parameter(Mandatory)]
        [string] $ShimApplicationDirectory
    )

    $scenarioRoot = [System.IO.Path]::Combine(
        $TemporaryRoot,
        "optional-dependencies")
    $workspace = [System.IO.Path]::Combine($scenarioRoot, "workspace")
    $sourceDirectory = [System.IO.Path]::Combine($workspace, "src")
    $markerDirectory = [System.IO.Path]::Combine($scenarioRoot, "markers")
    [System.IO.Directory]::CreateDirectory($sourceDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($markerDirectory) | Out-Null
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::Combine($workspace, "OptionalDependencies.csproj"),
        "<Project Sdk=`"Microsoft.NET.Sdk`" />`n")
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::Combine($sourceDirectory, "OptionalDependency.cs"),
        "internal static class OptionalDependency { private const string Value = `"optional-dependency-needle`"; }`n")

    $present = [System.IO.Path]::Combine($scenarioRoot, "present")
    $absent = [System.IO.Path]::Combine($scenarioRoot, "absent")
    $incompatible = [System.IO.Path]::Combine(
        $scenarioRoot,
        "incompatible")
    $failing = [System.IO.Path]::Combine($scenarioRoot, "failing")
    [System.IO.Directory]::CreateDirectory($absent) | Out-Null

    $scenarios = @()
    foreach ($definition in @(
            [pscustomobject]@{
                Name = "present"
                Directory = $present
                GitVersion = "git version 2.50.1"
                RgVersion = "ripgrep 15.2.0"
                ExitCode = 74
            },
            [pscustomobject]@{
                Name = "incompatible"
                Directory = $incompatible
                GitVersion = "git version 1.10.0"
                RgVersion = "ripgrep 12.1.0"
                ExitCode = 74
            },
            [pscustomobject]@{
                Name = "failing"
                Directory = $failing
                GitVersion = $null
                RgVersion = $null
                ExitCode = 74
            },
            [pscustomobject]@{
                Name = "shadowed"
                Directory = $workspace
                GitVersion = "git version 2.50.1"
                RgVersion = "ripgrep 15.2.0"
                ExitCode = 74
            })) {
        $markers = @()
        foreach ($command in @("git", "rg")) {
            $marker = [System.IO.Path]::Combine(
                $markerDirectory,
                "$($definition.Name)-$command")
            New-OptionalDependencyShim `
                -Directory $definition.Directory `
                -Command $command `
                -ApplicationDirectory $ShimApplicationDirectory `
                -Marker $marker `
                -Version $(if ($command -eq "git") {
                    $definition.GitVersion
                } else {
                    $definition.RgVersion
                }) `
                -ExitCode $definition.ExitCode | Out-Null
            $markers += $marker
        }

        $scenarios += [pscustomobject]@{
            Name = $definition.Name
            Path = $definition.Directory
            Markers = $markers
            GitVersion = $definition.GitVersion
            RgVersion = $definition.RgVersion
            ExitCode = $definition.ExitCode
        }
    }
    $scenarios += [pscustomobject]@{
        Name = "absent"
        Path = $absent
        Markers = @()
        GitVersion = $null
        RgVersion = $null
        ExitCode = 74
    }

    $expectedFile = $null
    $expectedLiteral = $null
    $expectedRegex = $null
    foreach ($scenario in $scenarios) {
        $environment = @{}
        foreach ($entry in $BaseEnvironment.GetEnumerator()) {
            $environment[$entry.Key] = $entry.Value
        }
        $environment["PATH"] = $scenario.Path
        $environment["DNAXI_OPTIONAL_DEPENDENCY_SHIM"] = "1"
        $environment["DNAXI_OPTIONAL_DEPENDENCY_GIT_MARKER"] =
            [System.IO.Path]::Combine(
                $markerDirectory,
                "$($scenario.Name)-git")
        $environment["DNAXI_OPTIONAL_DEPENDENCY_RG_MARKER"] =
            [System.IO.Path]::Combine(
                $markerDirectory,
                "$($scenario.Name)-rg")
        $environment["DNAXI_OPTIONAL_DEPENDENCY_GIT_VERSION"] =
            $scenario.GitVersion ?? ""
        $environment["DNAXI_OPTIONAL_DEPENDENCY_RG_VERSION"] =
            $scenario.RgVersion ?? ""
        $environment["DNAXI_OPTIONAL_DEPENDENCY_GIT_EXIT_CODE"] =
            [string] $scenario.ExitCode
        $environment["DNAXI_OPTIONAL_DEPENDENCY_RG_EXIT_CODE"] =
            [string] $scenario.ExitCode

        if ($scenario.Name -ne "absent") {
            foreach ($command in @("git", "rg")) {
                $shimName = if ([System.OperatingSystem]::IsWindows()) {
                    "$command.exe"
                }
                else {
                    $command
                }
                $shim = [System.IO.Path]::Combine(
                    $scenario.Path,
                    $shimName)
                $probe = Invoke-Captured `
                    -FileName $shim `
                    -Arguments @("--version") `
                    -Environment $environment `
                    -WorkingDirectory $scenarioRoot
                $expectedVersion = if ($command -eq "git") {
                    $scenario.GitVersion
                }
                else {
                    $scenario.RgVersion
                }
                if ([string]::IsNullOrWhiteSpace($expectedVersion)) {
                    if ($probe.ExitCode -ne $scenario.ExitCode) {
                        throw "$($scenario.Name) $command shim did not fail as configured."
                    }
                }
                else {
                    Assert-Success `
                        -Result $probe `
                        -Operation "$($scenario.Name) $command shim probe"
                    if ($probe.StandardOutput.Trim() -ne $expectedVersion) {
                        throw "$($scenario.Name) $command shim reported an unexpected version."
                    }
                }
            }
            foreach ($marker in $scenario.Markers) {
                [System.IO.File]::Delete($marker)
            }
        }

        $file = Invoke-Captured `
            -FileName $Executable `
            -Arguments @(
                "search", "file", "OptionalDependency",
                "--path", "src", "--limit", "20"
            ) `
            -Environment $environment `
            -WorkingDirectory $workspace
        $literal = Invoke-Captured `
            -FileName $Executable `
            -Arguments @(
                "search", "text", "optional-dependency-needle",
                "--case-sensitive", "--path", "src", "--full"
            ) `
            -Environment $environment `
            -WorkingDirectory $workspace
        $regex = Invoke-Captured `
            -FileName $Executable `
            -Arguments @(
                "search", "text", "optional-dependency-(needle|missing)",
                "--regex", "--case-sensitive", "--path", "src", "--full"
            ) `
            -Environment $environment `
            -WorkingDirectory $workspace
        foreach ($result in @($file, $literal, $regex)) {
            Assert-Success `
                -Result $result `
                -Operation "$($scenario.Name) non-Git discovery"
            if ($result.StandardError.Length -ne 0 -or
                $result.StandardOutput.Contains("`r")) {
                throw "$($scenario.Name) discovery emitted non-portable output."
            }
        }

        if ($null -eq $expectedFile) {
            $expectedFile = $file
            $expectedLiteral = $literal
            $expectedRegex = $regex
        }
        else {
            Assert-SameOutput `
                -Expected $expectedFile `
                -Actual $file `
                -Comparison "$($scenario.Name) file-search degradation"
            Assert-SameOutput `
                -Expected $expectedLiteral `
                -Actual $literal `
                -Comparison "$($scenario.Name) literal-search degradation"
            Assert-SameOutput `
                -Expected $expectedRegex `
                -Actual $regex `
                -Comparison "$($scenario.Name) regex-search degradation"
        }

        if (-not $file.StandardOutput.Contains("src/OptionalDependency.cs") -or
            -not $literal.StandardOutput.Contains("optional-dependency-needle") -or
            -not $regex.StandardOutput.Contains("optional-dependency-needle")) {
            throw "$($scenario.Name) discovery did not retain the built-in result."
        }

        $gitOnly = Invoke-Captured `
            -FileName $Executable `
            -Arguments @(
                "search", "file", "OptionalDependency", "--changed"
            ) `
            -Environment $environment `
            -WorkingDirectory $workspace
        if ($gitOnly.ExitCode -eq 0 -or
            -not $gitOnly.StandardOutput.Contains(
                "code: workspace.git_required")) {
            throw "$($scenario.Name) Git-only discovery did not return the non-Git capability error. Output: $($gitOnly.StandardOutput)"
        }
        if ($gitOnly.StandardError.Length -ne 0) {
            throw "$($scenario.Name) Git-only capability error wrote stderr."
        }

        $gitMarker = [System.IO.Path]::Combine(
            $markerDirectory,
            "$($scenario.Name)-git")
        if (Test-Path -LiteralPath $gitMarker) {
            throw "$($scenario.Name) non-Git discovery executed Git marker '$gitMarker'."
        }

        $rgMarker = [System.IO.Path]::Combine(
            $markerDirectory,
            "$($scenario.Name)-rg")
        $expectsRgProbe = $scenario.Name -in @(
            "present",
            "incompatible",
            "failing")
        if ($expectsRgProbe) {
            if (-not (Test-Path -LiteralPath $rgMarker)) {
                throw "$($scenario.Name) text discovery did not probe trusted rg."
            }

            $rgInvocations = @(
                [System.IO.File]::ReadAllLines($rgMarker) |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            )
            if (-not ($rgInvocations -contains '["--version"]')) {
                throw "$($scenario.Name) text discovery did not run the bounded rg version probe."
            }

            $rgSearches = @(
                $rgInvocations |
                    Where-Object { $_ -ne '["--version"]' }
            )
            if ($scenario.Name -eq "present") {
                if ($rgSearches.Count -eq 0 -or
                    -not ($rgSearches | Where-Object {
                        $_.Contains('"--files-with-matches"')
                    })) {
                    throw "present text discovery did not run the bounded rg accelerator."
                }
            }
            elseif ($rgSearches.Count -ne 0) {
                throw "$($scenario.Name) text discovery ran rg after an untrusted version result."
            }
        }
        elseif (Test-Path -LiteralPath $rgMarker) {
            throw "$($scenario.Name) discovery executed untrusted rg marker '$rgMarker'."
        }
    }
}

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
    throw "Expected one dnaxi .nupkg in '$resolvedPackageDirectory'; found $($packages.Count)."
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

    if ([string] $metadata.id -ne "dnaxi") {
        throw "Package ID is '$($metadata.id)', expected 'dnaxi'."
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

    $packagedSkill = $archive.Entries |
        Where-Object {
            $_.FullName.Replace('\', '/').StartsWith(
                "skills/",
                [System.StringComparison]::OrdinalIgnoreCase)
        } |
        Select-Object -First 1
    if ($null -ne $packagedSkill) {
        throw "Tool package must not carry Agent Skill entry '$($packagedSkill.FullName)'."
    }

    Assert-AssemblyVersionMetadata `
        -Archive $archive `
        -Version $version

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
    $symbolPackagedSkill = $symbolArchive.Entries |
        Where-Object {
            $_.FullName.Replace('\', '/').StartsWith(
                "skills/",
                [System.StringComparison]::OrdinalIgnoreCase)
        } |
        Select-Object -First 1
    if ($null -ne $symbolPackagedSkill) {
        throw "Symbol package must not carry Agent Skill entry '$($symbolPackagedSkill.FullName)'."
    }

    $symbolNuspecEntries = @(
        $symbolArchive.Entries |
            Where-Object { $_.FullName -like "*.nuspec" }
    )
    if ($symbolNuspecEntries.Count -ne 1) {
        throw (
            "Expected one symbol-package nuspec entry; found " +
            "$($symbolNuspecEntries.Count).")
    }

    [xml] $symbolNuspec = Read-ZipEntryText `
        -Archive $symbolArchive `
        -EntryName $symbolNuspecEntries[0].FullName
    $symbolMetadata = $symbolNuspec.package.metadata
    if ([string] $symbolMetadata.id -cne "dnaxi") {
        throw (
            "Symbol package ID is '$($symbolMetadata.id)', " +
            "expected 'dnaxi'.")
    }
    if ([string] $symbolMetadata.version -cne $version) {
        throw (
            "Symbol package version is '$($symbolMetadata.version)', " +
            "expected '$version'.")
    }
    if ([string] $symbolMetadata.packageTypes.packageType.name -cne
        "SymbolsPackage") {
        throw "Symbol package type must be SymbolsPackage."
    }
    if ([string] $symbolMetadata.repository.type -cne "git" -or
        [string] $symbolMetadata.repository.url -cne
            "https://github.com/chuyflores-dev/dotnet-axi.git" -or
        [string]::IsNullOrWhiteSpace(
            [string] $symbolMetadata.repository.commit)) {
        throw "Symbol package repository metadata is incomplete."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedCommit) -and
        [string] $symbolMetadata.repository.commit -cne $ExpectedCommit) {
        throw (
            "Symbol package repository commit is " +
            "'$($symbolMetadata.repository.commit)', expected " +
            "'$ExpectedCommit'.")
    }

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
    # Exercise the canonical no-install path before any persistent tool
    # lifecycle. The nearest NuGet configuration clears inherited sources, so
    # these exact-version candidate runs cannot fall back to public NuGet.
    $dnxSmokeWorkspace = [System.IO.Path]::Combine(
        $temporaryRoot,
        "dnx-smoke-workspace")
    [System.IO.Directory]::CreateDirectory($dnxSmokeWorkspace) | Out-Null
    $escapedPackageSource = [System.Security.SecurityElement]::Escape(
        $resolvedPackageDirectory)
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::Combine($dnxSmokeWorkspace, "NuGet.Config"),
        "<configuration><packageSources><clear/><add key=`"candidate`" value=`"$escapedPackageSource`"/></packageSources></configuration>")
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::Combine($dnxSmokeWorkspace, "DnxSmoke.cs"),
        "namespace DnxSmoke; internal static class Sample { internal static void Run() => System.Console.WriteLine(`"dnaxi dnx smoke`" ); }")
    $dnxSmokeEnvironment = @{
        DOTNET_CLI_HOME = [System.IO.Path]::Combine(
            $temporaryRoot,
            "dnx-smoke-home")
        DOTNET_CLI_TELEMETRY_OPTOUT = "1"
        DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "1"
        DOTNET_NOLOGO = "1"
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
        NUGET_PACKAGES = [System.IO.Path]::Combine(
            $temporaryRoot,
            "dnx-smoke-packages")
    }
    $dnxCandidatePrefix = @(
        "dnaxi@$version",
        "--source", $resolvedPackageDirectory,
        "--no-http-cache", "--verbosity", "quiet",
        "--")

    $dnxSmokeVersion = Invoke-Captured `
        -FileName $dnx `
        -Arguments ($dnxCandidatePrefix + @("--version")) `
        -Environment $dnxSmokeEnvironment `
        -WorkingDirectory $dnxSmokeWorkspace
    Assert-VersionOutput `
        -Result $dnxSmokeVersion `
        -Version $version `
        -RequireEmptyStandardError $false

    $dnxSmokeHelp = Invoke-Captured `
        -FileName $dnx `
        -Arguments ($dnxCandidatePrefix + @("--help")) `
        -Environment $dnxSmokeEnvironment `
        -WorkingDirectory $dnxSmokeWorkspace
    Assert-HelpOutput `
        -Result $dnxSmokeHelp `
        -Version $version `
        -RequireEmptyStandardError $false

    $dnxSmokeHome = Invoke-Captured `
        -FileName $dnx `
        -Arguments $dnxCandidatePrefix `
        -Environment $dnxSmokeEnvironment `
        -WorkingDirectory $dnxSmokeWorkspace
    Assert-HomeOutput -Result $dnxSmokeHome -Version $version

    $dnxSmokeFile = Invoke-Captured `
        -FileName $dnx `
        -Arguments ($dnxCandidatePrefix + @(
            "search", "file", "DnxSmoke.cs", "--path", ".", "--limit", "20")) `
        -Environment $dnxSmokeEnvironment `
        -WorkingDirectory $dnxSmokeWorkspace
    Assert-PassiveCommandOutput `
        -Result $dnxSmokeFile `
        -Command "search file"

    $dnxSmokeText = Invoke-Captured `
        -FileName $dnx `
        -Arguments ($dnxCandidatePrefix + @(
            "search", "text", "dnaxi dnx smoke", "--path", ".", "--limit", "20")) `
        -Environment $dnxSmokeEnvironment `
        -WorkingDirectory $dnxSmokeWorkspace
    Assert-PassiveCommandOutput `
        -Result $dnxSmokeText `
        -Command "search text"

    $dnxSmokeSyntax = Invoke-Captured `
        -FileName $dnx `
        -Arguments ($dnxCandidatePrefix + @(
            "search", "syntax", "invocation", "--name", "WriteLine",
            "--path", ".", "--limit", "20")) `
        -Environment $dnxSmokeEnvironment `
        -WorkingDirectory $dnxSmokeWorkspace
    Assert-PassiveCommandOutput `
        -Result $dnxSmokeSyntax `
        -Command "search syntax invocation"

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
            "dnaxi",
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

    Assert-OptionalDependencyScenarios `
        -Executable $globalExecutable `
        -BaseEnvironment $globalEnvironment `
        -TemporaryRoot $temporaryRoot `
        -ShimApplicationDirectory ([System.IO.Path]::GetFullPath(
            [System.IO.Path]::Combine(
                $PSScriptRoot,
                "..",
                "tests",
                "DotNetAxi.DotNet.ProcessTestApp",
                "bin",
                "Release",
                "net10.0")))

    $globalHelp = Invoke-Captured `
        -FileName "dnaxi" `
        -Arguments @("--help") `
        -Environment $globalEnvironment
    Assert-HelpOutput -Result $globalHelp -Version $version

    $globalUpdate = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool",
            "update",
            "--global",
            "dnaxi",
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
            "dnaxi",
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
    Assert-HelpOutput -Result $localHelp -Version $version

    $localUpdate = Invoke-Captured `
        -FileName $dotnet `
        -Arguments @(
            "tool",
            "update",
            "--local",
            "dnaxi",
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
        DOTNET_CLI_TELEMETRY_OPTOUT = "1"
        DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "1"
        DOTNET_NOLOGO = "1"
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
        NUGET_PACKAGES = [System.IO.Path]::Combine(
            $temporaryRoot,
            "dnx-packages")
    }
    $oneShotVersion = Invoke-Captured `
        -FileName $dnx `
        -Arguments @(
            "dnaxi@$version",
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
            "dnaxi@$version",
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
        -Version $version `
        -RequireEmptyStandardError $false

    $oneShotWorkspace = [System.IO.Path]::Combine(
        $temporaryRoot,
        "one-shot-workspace")
    [System.IO.Directory]::CreateDirectory($oneShotWorkspace) | Out-Null
    $oneShotHome = Invoke-Captured `
        -FileName $dnx `
        -Arguments @(
            "dnaxi@$version",
            "--source",
            $resolvedPackageDirectory,
            "--no-http-cache",
            "--verbosity",
            "quiet",
            "--"
        ) `
        -Environment $dnxEnvironment `
        -WorkingDirectory $oneShotWorkspace
    Assert-HomeOutput -Result $oneShotHome -Version $version

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
            "dnaxi",
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
            "dnaxi"
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

Write-Host "Verified dnaxi $version tool package, symbols, global/local lifecycle, and dnx parity."
