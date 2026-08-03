using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
        var capture = new WorkspaceSnapshotCapture(files, values);

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
        Assert.Matches("^ws_[0-9a-f]{64}$", snapshot.Identity);
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
                    @"src\A.cs",
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
                    @"src\App.csproj"),
            ]);
        var capturer = new WorkspaceSnapshotCapturer();

        Assert.Throws<ArgumentException>(
            () => capturer.Capture(duplicateFiles));
        Assert.Throws<ArgumentException>(
            () => capturer.Capture(duplicateValues));
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
        IEnumerable<WorkspaceSnapshotValueInput>? values = null) =>
        new WorkspaceSnapshotCapturer().Capture(
            new WorkspaceSnapshotCapture(files ?? [], values ?? []));

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
        string order)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? "dotnet",
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(
            typeof(WorkspaceSnapshotCapturerTests).Assembly.Location);
        startInfo.ArgumentList.Add("snapshot-identity");
        startInfo.ArgumentList.Add(culture);
        startInfo.ArgumentList.Add(order);
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);

        Assert.True(
            process.ExitCode == 0,
            $"Snapshot process failed.\n{await standardError}");
        return (await standardOutput).Trim();
    }
}

internal static class SnapshotIdentityProcess
{
    public static int Run(string[] args)
    {
        if (args.Length != 3
            || !args[0].Equals("snapshot-identity", StringComparison.Ordinal)
            || args[2] is not ("forward" or "reverse"))
        {
            return 64;
        }

        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(args[1]);
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        var reverse = args[2].Equals("reverse", StringComparison.Ordinal);
        var snapshot = SnapshotIdentityFixture.Capture(
            reverse,
            windowsSeparators: reverse);
        Console.WriteLine(snapshot.Identity);
        return 0;
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
            new WorkspaceSnapshotCapture(files, values));
    }
}
