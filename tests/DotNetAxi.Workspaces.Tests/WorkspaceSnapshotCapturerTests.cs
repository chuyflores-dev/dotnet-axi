using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Contracts;
using DotNetAxi.DotNet;

namespace DotNetAxi.Workspaces.Tests;

public sealed class WorkspaceSnapshotCapturerTests
{
    [Fact]
    public void Capture_discloses_only_the_observed_immutable_scope()
    {
        byte[] observedContent = [1, 2, 3];
        var observedFile = new WorkspaceSnapshotFileInput(
            WorkspaceSnapshotFileKind.SelectedSource,
            @"src\Observed.cs",
            observedContent);
        var files = new List<WorkspaceSnapshotFileInput> { observedFile };
        var values = new List<WorkspaceSnapshotValueInput>();
        var capture = new WorkspaceSnapshotCapture(
            files,
            values,
            new WorkspaceSnapshotEntryPointInput(
                WorkspaceEntryPointKind.Project,
                @"src\.\Observed.csproj"));

        observedContent[0] = 9;
        files.Add(new WorkspaceSnapshotFileInput(
            WorkspaceSnapshotFileKind.AdditionalFile,
            "unobserved.json",
            new byte[] { 4 }));
        values.Add(new WorkspaceSnapshotValueInput(
            WorkspaceSnapshotValueKind.TargetFramework,
            "target-framework",
            "unobserved"));

        var snapshot = new WorkspaceSnapshotCapturer().Capture(capture);

        var file = Assert.Single(snapshot.Scope.Files);
        Assert.Equal(WorkspaceSnapshotFileKind.SelectedSource, file.Kind);
        Assert.Equal("src/Observed.cs", file.Path);
        Assert.Equal(ContentHash([1, 2, 3]), file.ContentHash);
        Assert.Empty(snapshot.Scope.Values);
        Assert.Equal(
            new WorkspaceSnapshotEntryPointScope(
                WorkspaceEntryPointKind.Project,
                "src/Observed.csproj"),
            snapshot.Scope.SelectedEntryPoint);
        Assert.Matches("^ws_[0-9a-f]{64}$", snapshot.Identity);
    }

    [Theory]
    [InlineData(WorkspaceSnapshotFileKind.MetadataReference)]
    [InlineData(WorkspaceSnapshotFileKind.NuGetConfiguration)]
    public void Mutating_observed_dependency_bytes_changes_identity_and_scope(
        WorkspaceSnapshotFileKind kind)
    {
        var path = kind is WorkspaceSnapshotFileKind.MetadataReference
            ? "../packages/Library.dll"
            : "NuGet.config";
        var before = Capture(
            files:
            [
                new WorkspaceSnapshotFileInput(
                    kind,
                    path,
                    "before"u8.ToArray()),
            ]);
        var after = Capture(
            files:
            [
                new WorkspaceSnapshotFileInput(
                    kind,
                    path,
                    "after"u8.ToArray()),
            ]);

        Assert.NotEqual(before.Identity, after.Identity);
        var scope = Assert.Single(before.Scope.Files);
        Assert.Equal(kind, scope.Kind);
        Assert.Equal(path, scope.Path);
        Assert.Equal(ContentHash("before"u8), scope.ContentHash);
    }

    [Theory]
    [MemberData(nameof(FileKinds))]
    public void Mutating_each_documented_file_input_changes_the_identity(
        WorkspaceSnapshotFileKind kind)
    {
        var before = Capture(
            files:
            [
                new WorkspaceSnapshotFileInput(
                    kind,
                    "scope/input",
                    "before"u8.ToArray()),
            ]);
        var after = Capture(
            files:
            [
                new WorkspaceSnapshotFileInput(
                    kind,
                    "scope/input",
                    "after"u8.ToArray()),
            ]);

        Assert.NotEqual(before.Identity, after.Identity);
    }

    [Theory]
    [MemberData(nameof(ValueKinds))]
    public void Mutating_each_documented_value_input_changes_the_identity(
        WorkspaceSnapshotValueKind kind)
    {
        var before = Capture(
            values:
            [
                new WorkspaceSnapshotValueInput(
                    kind,
                    "input",
                    "before",
                    @"src\App.csproj"),
            ]);
        var after = Capture(
            values:
            [
                new WorkspaceSnapshotValueInput(
                    kind,
                    "input",
                    "after",
                    "src/App.csproj"),
            ]);

        Assert.NotEqual(before.Identity, after.Identity);
    }

    [Fact]
    public void Ordering_and_platform_separators_do_not_change_the_identity()
    {
        WorkspaceSnapshotFileInput[] files =
        [
            new(
                WorkspaceSnapshotFileKind.SelectedSource,
                @"src\Z.cs",
                "z"u8.ToArray()),
            new(
                WorkspaceSnapshotFileKind.SelectedSource,
                @"src\A.cs",
                "a"u8.ToArray()),
            new(
                WorkspaceSnapshotFileKind.Project,
                @"src\App.csproj",
                "project"u8.ToArray()),
        ];
        WorkspaceSnapshotValueInput[] values =
        [
            new(
                WorkspaceSnapshotValueKind.ExplicitMsBuildProperty,
                "Zeta",
                "last",
                @"src\App.csproj"),
            new(
                WorkspaceSnapshotValueKind.ExplicitMsBuildProperty,
                "Alpha",
                "first",
                @"src\App.csproj"),
        ];
        var first = Capture(files, values);
        var second = Capture(
            files.Reverse().Select(static file =>
                new WorkspaceSnapshotFileInput(
                    file.Kind,
                    file.Path.Replace('/', '\\'),
                    FileContent(file))),
            values.Reverse().Select(static value =>
                new WorkspaceSnapshotValueInput(
                    value.Kind,
                    value.Name,
                    ValueText(value.Name),
                    value.ScopePath?.Replace('/', '\\'))));

        Assert.Equal(first.Identity, second.Identity);
        Assert.Equal(
            ["src/A.cs", "src/Z.cs"],
            first.Scope.Files
                .Where(static file =>
                    file.Kind == WorkspaceSnapshotFileKind.SelectedSource)
                .Select(static file => file.Path));
        Assert.Equal(
            ["Alpha", "Zeta"],
            first.Scope.Values.Select(static value => value.Name));
    }

    [Fact]
    public void Length_framing_and_domains_prevent_ambiguous_collisions()
    {
        var splitAfterFirstCharacter = Capture(
            values:
            [
                new WorkspaceSnapshotValueInput(
                    WorkspaceSnapshotValueKind.Configuration,
                    "a",
                    "bc"),
            ]);
        var splitAfterSecondCharacter = Capture(
            values:
            [
                new WorkspaceSnapshotValueInput(
                    WorkspaceSnapshotValueKind.Configuration,
                    "ab",
                    "c"),
            ]);
        var selectedSource = Capture(
            files:
            [
                new WorkspaceSnapshotFileInput(
                    WorkspaceSnapshotFileKind.SelectedSource,
                    "same",
                    "content"u8.ToArray()),
            ]);
        var linkedSource = Capture(
            files:
            [
                new WorkspaceSnapshotFileInput(
                    WorkspaceSnapshotFileKind.LinkedSource,
                    "same",
                    "content"u8.ToArray()),
            ]);

        Assert.NotEqual(
            splitAfterFirstCharacter.Identity,
            splitAfterSecondCharacter.Identity);
        Assert.NotEqual(selectedSource.Identity, linkedSource.Identity);
        Assert.NotEqual(selectedSource.Identity, splitAfterFirstCharacter.Identity);
    }

    [Fact]
    public void Path_name_and_scope_are_identity_inputs()
    {
        var originalFile = Capture(
            files:
            [
                new WorkspaceSnapshotFileInput(
                    WorkspaceSnapshotFileKind.Project,
                    "src/App.csproj",
                    "same"u8.ToArray()),
            ]);
        var movedFile = Capture(
            files:
            [
                new WorkspaceSnapshotFileInput(
                    WorkspaceSnapshotFileKind.Project,
                    "other/App.csproj",
                    "same"u8.ToArray()),
            ]);
        var originalValue = Capture(
            values:
            [
                new WorkspaceSnapshotValueInput(
                    WorkspaceSnapshotValueKind.ExplicitMsBuildProperty,
                    "DefineConstants",
                    "TRACE",
                    "src/App.csproj"),
            ]);
        var renamedValue = Capture(
            values:
            [
                new WorkspaceSnapshotValueInput(
                    WorkspaceSnapshotValueKind.ExplicitMsBuildProperty,
                    "NoWarn",
                    "TRACE",
                    "src/App.csproj"),
            ]);
        var movedValue = Capture(
            values:
            [
                new WorkspaceSnapshotValueInput(
                    WorkspaceSnapshotValueKind.ExplicitMsBuildProperty,
                    "DefineConstants",
                    "TRACE",
                    "other/App.csproj"),
            ]);

        Assert.NotEqual(originalFile.Identity, movedFile.Identity);
        Assert.NotEqual(originalValue.Identity, renamedValue.Identity);
        Assert.NotEqual(originalValue.Identity, movedValue.Identity);
    }

    [Fact]
    public void Selected_entry_point_kind_and_path_are_identity_scope_inputs()
    {
        var explicitSelection = Capture(
            selectedEntryPoint: new WorkspaceSnapshotEntryPointInput(
                new WorkspaceSelection(
                    WorkspaceEntryPointKind.Solution,
                    "src/App.sln",
                    WorkspaceSelectionSource.ExplicitSolution)));
        var configuredSelection = Capture(
            selectedEntryPoint: new WorkspaceSnapshotEntryPointInput(
                new WorkspaceSelection(
                    WorkspaceEntryPointKind.Solution,
                    "src/App.sln",
                    WorkspaceSelectionSource.RepositoryConfiguration)));
        var selectedProject = Capture(
            selectedEntryPoint: new WorkspaceSnapshotEntryPointInput(
                WorkspaceEntryPointKind.Project,
                "src/App.sln"));
        var movedSolution = Capture(
            selectedEntryPoint: new WorkspaceSnapshotEntryPointInput(
                WorkspaceEntryPointKind.Solution,
                "other/App.sln"));

        Assert.Equal(explicitSelection.Identity, configuredSelection.Identity);
        Assert.Equal(
            new WorkspaceSnapshotEntryPointScope(
                WorkspaceEntryPointKind.Solution,
                "src/App.sln"),
            explicitSelection.Scope.SelectedEntryPoint);
        Assert.NotEqual(explicitSelection.Identity, selectedProject.Identity);
        Assert.NotEqual(explicitSelection.Identity, movedSolution.Identity);
    }

    [Fact]
    public void Path_aliases_are_canonical_and_external_identities_are_preserved()
    {
        var aliases = Capture(
            files:
            [
                new WorkspaceSnapshotFileInput(
                    WorkspaceSnapshotFileKind.MetadataReference,
                    @"..\packages\\.\refs\..\Library.dll",
                    "reference"u8.ToArray()),
            ],
            values:
            [
                new WorkspaceSnapshotValueInput(
                    WorkspaceSnapshotValueKind.Configuration,
                    "configuration",
                    "Debug",
                    @".\src\\nested\..\App.csproj"),
            ],
            selectedEntryPoint: new WorkspaceSnapshotEntryPointInput(
                WorkspaceEntryPointKind.Project,
                "./src//nested/../App.csproj"));
        var canonical = Capture(
            files:
            [
                new WorkspaceSnapshotFileInput(
                    WorkspaceSnapshotFileKind.MetadataReference,
                    "../packages/Library.dll",
                    "reference"u8.ToArray()),
            ],
            values:
            [
                new WorkspaceSnapshotValueInput(
                    WorkspaceSnapshotValueKind.Configuration,
                    "configuration",
                    "Debug",
                    "src/App.csproj"),
            ],
            selectedEntryPoint: new WorkspaceSnapshotEntryPointInput(
                WorkspaceEntryPointKind.Project,
                "src/App.csproj"));

        Assert.Equal(canonical.Identity, aliases.Identity);
        Assert.Equal(
            "../packages/Library.dll",
            Assert.Single(aliases.Scope.Files).Path);
        Assert.Equal(
            "src/App.csproj",
            Assert.Single(aliases.Scope.Values).ScopePath);
        Assert.Equal(
            "src/App.csproj",
            aliases.Scope.SelectedEntryPoint?.Path);
    }

    [Theory]
    [InlineData("/machine/work/App.csproj")]
    [InlineData(@"\machine\work\App.csproj")]
    [InlineData(@"\\server\share\App.csproj")]
    [InlineData(@"C:\machine\work\App.csproj")]
    [InlineData(@"C:machine\work\App.csproj")]
    public void Rooted_and_machine_specific_path_identities_are_rejected(
        string path)
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkspaceSnapshotFileInput(
                WorkspaceSnapshotFileKind.Project,
                path,
                ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentException>(() =>
            new WorkspaceSnapshotValueInput(
                WorkspaceSnapshotValueKind.Configuration,
                "configuration",
                "Debug",
                path));
        Assert.Throws<ArgumentException>(() =>
            new WorkspaceSnapshotEntryPointInput(
                WorkspaceEntryPointKind.Project,
                path));
    }

    [Fact]
    public void Duplicate_normalized_scope_keys_are_rejected()
    {
        var duplicateFiles = new WorkspaceSnapshotCapture(
            [
                new WorkspaceSnapshotFileInput(
                    WorkspaceSnapshotFileKind.SelectedSource,
                    "src/A.cs",
                    "first"u8.ToArray()),
                new WorkspaceSnapshotFileInput(
                    WorkspaceSnapshotFileKind.SelectedSource,
                    "src//generated/../A.cs",
                    "second"u8.ToArray()),
            ],
            []);
        var duplicateValues = new WorkspaceSnapshotCapture(
            [],
            [
                new WorkspaceSnapshotValueInput(
                    WorkspaceSnapshotValueKind.ExplicitMsBuildProperty,
                    "Property",
                    "first",
                    "src/App.csproj"),
                new WorkspaceSnapshotValueInput(
                    WorkspaceSnapshotValueKind.ExplicitMsBuildProperty,
                    "Property",
                    "second",
                    "src/./nested/../App.csproj"),
            ]);
        var capturer = new WorkspaceSnapshotCapturer();

        Assert.Throws<ArgumentException>(
            () => capturer.Capture(duplicateFiles));
        Assert.Throws<ArgumentException>(
            () => capturer.Capture(duplicateValues));
    }

    [Fact]
    public void Duplicate_paths_follow_platform_case_semantics()
    {
        var capture = new WorkspaceSnapshotCapture(
            [
                new WorkspaceSnapshotFileInput(
                    WorkspaceSnapshotFileKind.SelectedSource,
                    "src/A.cs",
                    "first"u8.ToArray()),
                new WorkspaceSnapshotFileInput(
                    WorkspaceSnapshotFileKind.SelectedSource,
                    "src/a.cs",
                    "second"u8.ToArray()),
            ],
            []);
        var capturer = new WorkspaceSnapshotCapturer();

        if (OperatingSystem.IsWindows())
        {
            Assert.Throws<ArgumentException>(() => capturer.Capture(capture));
        }
        else
        {
            Assert.Equal(2, capturer.Capture(capture).Scope.Files.Count);
        }
    }

    [Fact]
    public async Task Identity_is_stable_across_fresh_processes_and_cultures()
    {
        var first = await CaptureInFreshProcessAsync("en-US", "forward");
        var second = await CaptureInFreshProcessAsync("tr-TR", "reverse");
        var local = SnapshotIdentityFixture.Capture(
            reverse: false,
            windowsSeparators: false);

        Assert.Equal(first, second);
        Assert.Equal(local.Identity, first);
    }

    [Fact]
    public async Task Fresh_process_timeout_terminates_the_child_and_fails_clearly()
    {
        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            CaptureInFreshProcessAsync(
                "en-US",
                "hang",
                TimeSpan.FromMilliseconds(250)));

        Assert.Contains(
            "process tree was terminated",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fresh_process_containment_terminates_a_descendant_after_the_root_exits()
    {
        var output = await CaptureInFreshProcessAsync(
            "en-US",
            "exit-with-grandchild",
            TimeSpan.FromSeconds(1));

        Assert.Empty(output);
    }

    public static TheoryData<WorkspaceSnapshotFileKind> FileKinds =>
        Enum.GetValues<WorkspaceSnapshotFileKind>()
            .Aggregate(
                new TheoryData<WorkspaceSnapshotFileKind>(),
                static (data, kind) =>
                {
                    data.Add(kind);
                    return data;
                });

    public static TheoryData<WorkspaceSnapshotValueKind> ValueKinds =>
        Enum.GetValues<WorkspaceSnapshotValueKind>()
            .Aggregate(
                new TheoryData<WorkspaceSnapshotValueKind>(),
                static (data, kind) =>
                {
                    data.Add(kind);
                    return data;
                });

    private static WorkspaceSnapshot Capture(
        IEnumerable<WorkspaceSnapshotFileInput>? files = null,
        IEnumerable<WorkspaceSnapshotValueInput>? values = null,
        WorkspaceSnapshotEntryPointInput? selectedEntryPoint = null) =>
        new WorkspaceSnapshotCapturer().Capture(
            new WorkspaceSnapshotCapture(
                files ?? [],
                values ?? [],
                selectedEntryPoint));

    private static byte[] FileContent(WorkspaceSnapshotFileInput file) =>
        file.Path switch
        {
            "src/Z.cs" => "z"u8.ToArray(),
            "src/A.cs" => "a"u8.ToArray(),
            "src/App.csproj" => "project"u8.ToArray(),
            _ => throw new InvalidOperationException(),
        };

    private static string ValueText(string name) =>
        name switch
        {
            "Zeta" => "last",
            "Alpha" => "first",
            _ => throw new InvalidOperationException(),
        };

    private static string ContentHash(ReadOnlySpan<byte> content) =>
        $"sha256_{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}";

    private static async Task<string> CaptureInFreshProcessAsync(
        string culture,
        string order,
        TimeSpan? timeoutAfter = null)
    {
        var timeoutDuration = timeoutAfter ?? TimeSpan.FromSeconds(30);
        var configuredHost = Environment.GetEnvironmentVariable(
            "DOTNET_HOST_PATH");
        var host = await new DotNetHostResolver().ResolveAsync(
            new DotNetHostResolutionRequest(
                AppContext.BaseDirectory,
                configuredHost is not null
                && Path.IsPathFullyQualified(configuredHost)
                    ? configuredHost
                    : null));
        Assert.True(
            host.IsResolved,
            $"Could not resolve dotnet host: {host.Failure?.Code}");
        var result = await new ProcessRunner().RunAsync(
            new ProcessRunRequest(
                host.ExecutablePath!,
                AppContext.BaseDirectory,
                [
                    typeof(WorkspaceSnapshotCapturerTests).Assembly.Location,
                    "snapshot-identity",
                    culture,
                    order,
                ],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                    ["DOTNET_NOLOGO"] = "1",
                    ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
                },
                new ProcessOutputLimits(
                    standardOutputCharacters: 64 * 1024,
                    standardErrorCharacters: 64 * 1024),
                timeoutDuration),
            CancellationToken.None);
        if (result.Outcome is ProcessRunOutcome.TimedOut)
        {
            throw new TimeoutException(
                $"Snapshot process timed out after {timeoutDuration} and its process tree was terminated.");
        }

        Assert.True(
            result.Outcome is ProcessRunOutcome.Completed
            && result.Exit?.ExitCode == 0,
            $"Snapshot process failed ({result.Outcome}).\n{result.StandardError.Text}");
        return result.StandardOutput.Text.Trim();
    }
}

internal static class SnapshotIdentityProcess
{
    public static int Run(string[] args)
    {
        if (args.Length != 3
            || !args[0].Equals("snapshot-identity", StringComparison.Ordinal)
            || args[2] is not (
                "forward" or "reverse" or "hang" or "exit-with-grandchild"))
        {
            return 64;
        }

        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(args[1]);
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        if (args[2] is "hang")
        {
            Thread.Sleep(Timeout.Infinite);
        }

        if (args[2] is "exit-with-grandchild")
        {
            return StartGrandchild(args[1]) ? 0 : 70;
        }

        var reverse = args[2].Equals("reverse", StringComparison.Ordinal);
        var snapshot = SnapshotIdentityFixture.Capture(
            reverse,
            windowsSeparators: reverse);
        Console.WriteLine(snapshot.Identity);
        return 0;
    }

    private static bool StartGrandchild(string culture)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? "dotnet",
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(
            typeof(WorkspaceSnapshotCapturerTests).Assembly.Location);
        startInfo.ArgumentList.Add("snapshot-identity");
        startInfo.ArgumentList.Add(culture);
        startInfo.ArgumentList.Add("hang");
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using var process = Process.Start(startInfo);
        return process is not null;
    }
}

internal static class SnapshotIdentityFixture
{
    public static WorkspaceSnapshot Capture(
        bool reverse,
        bool windowsSeparators)
    {
        var separator = windowsSeparators ? '\\' : '/';
        var files = Enum.GetValues<WorkspaceSnapshotFileKind>()
            .Select((kind, index) => new WorkspaceSnapshotFileInput(
                kind,
                $"scope{separator}file-{index:D2}.input",
                Encoding.UTF8.GetBytes($"{kind}:content")))
            .ToArray();
        var values = Enum.GetValues<WorkspaceSnapshotValueKind>()
            .Select((kind, index) => new WorkspaceSnapshotValueInput(
                kind,
                $"input-{index:D2}",
                $"{kind}:value",
                $"scope{separator}App.csproj"))
            .ToArray();
        if (reverse)
        {
            Array.Reverse(files);
            Array.Reverse(values);
        }

        return new WorkspaceSnapshotCapturer().Capture(
            new WorkspaceSnapshotCapture(
                files,
                values,
                new WorkspaceSnapshotEntryPointInput(
                    WorkspaceEntryPointKind.Solution,
                    $"scope{separator}App.sln")));
    }
}
