using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DotNetAxi.Workspaces;

public sealed class WorkspaceSnapshotCapturer
{
    private static readonly byte[] SnapshotDomain =
        "dotnet-axi/workspace-snapshot"u8.ToArray();

    private static readonly byte[] FormatVersion = "v2"u8.ToArray();
    private static readonly byte[] EntryPointDomain =
        "selected-entry-point"u8.ToArray();
    private static readonly byte[] FileDomain = "file"u8.ToArray();
    private static readonly byte[] ValueDomain = "value"u8.ToArray();

    public WorkspaceSnapshot Capture(WorkspaceSnapshotCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        var files = capture.Files
            .Select(static input => new FileEntry(
                input,
                FileKindToken(input.Kind)))
            .OrderBy(static entry => entry.KindToken, StringComparer.Ordinal)
            .ThenBy(
                static entry => entry.Input.Path,
                StringComparer.Ordinal)
            .ToArray();
        var values = capture.Values
            .Select(static input => new ValueEntry(
                input,
                ValueKindToken(input.Kind)))
            .OrderBy(static entry => entry.KindToken, StringComparer.Ordinal)
            .ThenBy(
                static entry => entry.Input.ScopePath,
                StringComparer.Ordinal)
            .ThenBy(
                static entry => entry.Input.Name,
                StringComparer.Ordinal)
            .ToArray();

        RejectDuplicateFiles(files);
        RejectDuplicateValues(values);

        var entryPoint = capture.SelectedEntryPoint is null
            ? null
            : new EntryPointEntry(
                capture.SelectedEntryPoint,
                EntryPointKindToken(capture.SelectedEntryPoint.Kind));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFrame(hash, SnapshotDomain);
        AppendFrame(hash, FormatVersion);
        AppendCount(hash, entryPoint is null ? 0 : 1);
        if (entryPoint is not null)
        {
            AppendFrame(hash, EntryPointDomain);
            AppendFrame(hash, entryPoint.KindTokenBytes);
            AppendFrame(hash, entryPoint.Input.PathBytes);
        }

        AppendCount(hash, files.Length);
        foreach (var file in files)
        {
            AppendFrame(hash, FileDomain);
            AppendFrame(hash, file.KindTokenBytes);
            AppendFrame(hash, file.Input.PathBytes);
            AppendFrame(hash, file.Input.Content);
        }

        AppendCount(hash, values.Length);
        foreach (var value in values)
        {
            AppendFrame(hash, ValueDomain);
            AppendFrame(hash, value.KindTokenBytes);
            AppendOptionalFrame(hash, value.Input.ScopePathBytes);
            AppendFrame(hash, value.Input.NameBytes);
            AppendFrame(hash, value.Input.ValueBytes);
        }

        var identity = $"ws_{LowerHex(hash.GetHashAndReset())}";
        var scope = new WorkspaceSnapshotScope(
            files.Select(static entry => new WorkspaceSnapshotFileScope(
                entry.Input.Kind,
                entry.Input.Path,
                ContentHash(entry.Input.Content))),
            values.Select(static entry => new WorkspaceSnapshotValueScope(
                entry.Input.Kind,
                entry.Input.Name,
                entry.Input.ScopePath,
                ContentHash(entry.Input.ValueBytes))),
            entryPoint is null
                ? null
                : new WorkspaceSnapshotEntryPointScope(
                    entryPoint.Input.Kind,
                    entryPoint.Input.Path));
        return new WorkspaceSnapshot(identity, scope);
    }

    private static void RejectDuplicateFiles(IEnumerable<FileEntry> files)
    {
        var seen = new Dictionary<
            WorkspaceSnapshotFileKind,
            HashSet<string>>();
        foreach (var file in files)
        {
            if (!seen.TryGetValue(file.Input.Kind, out var paths))
            {
                paths = new HashSet<string>(
                    WorkspacePathResolver.PathComparer());
                seen.Add(file.Input.Kind, paths);
            }

            if (!paths.Add(file.Input.Path))
            {
                throw new ArgumentException(
                    $"The snapshot contains duplicate {file.Input.Kind} file input '{file.Input.Path}'.",
                    "capture");
            }
        }
    }

    private static void RejectDuplicateValues(IEnumerable<ValueEntry> values)
    {
        var seen = new Dictionary<
            (WorkspaceSnapshotValueKind Kind, string Name),
            HashSet<string?>>();
        foreach (var value in values)
        {
            var key = (value.Input.Kind, value.Input.Name);
            if (!seen.TryGetValue(key, out var scopePaths))
            {
                scopePaths = new HashSet<string?>(
                    WorkspacePathResolver.PathComparer());
                seen.Add(key, scopePaths);
            }

            if (!scopePaths.Add(value.Input.ScopePath))
            {
                throw new ArgumentException(
                    $"The snapshot contains duplicate {value.Input.Kind} value input '{value.Input.Name}'.",
                    "capture");
            }
        }
    }

    private static void AppendCount(IncrementalHash hash, int count)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, checked((ulong)count));
        AppendFrame(hash, bytes);
    }

    private static void AppendOptionalFrame(
        IncrementalHash hash,
        ReadOnlySpan<byte> value)
    {
        Span<byte> presence = stackalloc byte[1];
        presence[0] = value.IsEmpty ? (byte)0 : (byte)1;
        AppendFrame(hash, presence);
        if (!value.IsEmpty)
        {
            AppendFrame(hash, value);
        }
    }

    private static void AppendFrame(
        IncrementalHash hash,
        ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(
            length,
            checked((ulong)value.Length));
        hash.AppendData(length);
        hash.AppendData(value);
    }

    private static string ContentHash(ReadOnlySpan<byte> content) =>
        $"sha256_{LowerHex(SHA256.HashData(content))}";

    private static string LowerHex(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(value).ToLowerInvariant();

    private static string FileKindToken(WorkspaceSnapshotFileKind kind) =>
        kind switch
        {
            WorkspaceSnapshotFileKind.SelectedSource => "source/selected",
            WorkspaceSnapshotFileKind.LinkedSource => "source/linked",
            WorkspaceSnapshotFileKind.AdditionalFile => "source/additional",
            WorkspaceSnapshotFileKind.AnalyzerConfiguration =>
                "source/analyzer-configuration",
            WorkspaceSnapshotFileKind.Solution => "workspace/solution",
            WorkspaceSnapshotFileKind.Project => "workspace/project",
            WorkspaceSnapshotFileKind.MsBuildImport =>
                "workspace/msbuild-import",
            WorkspaceSnapshotFileKind.GlobalJson =>
                "workspace/global-json",
            WorkspaceSnapshotFileKind.NuGetConfiguration =>
                "dependencies/nuget-configuration",
            WorkspaceSnapshotFileKind.NuGetAssets =>
                "dependencies/nuget-assets",
            WorkspaceSnapshotFileKind.NuGetLock =>
                "dependencies/nuget-lock",
            WorkspaceSnapshotFileKind.MetadataReference =>
                "compilation/metadata-reference",
            WorkspaceSnapshotFileKind.GeneratedSourceInput =>
                "generation/source-input",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The workspace snapshot file kind is not defined."),
        };

    private static string EntryPointKindToken(WorkspaceEntryPointKind kind) =>
        kind switch
        {
            WorkspaceEntryPointKind.Solution => "workspace/solution",
            WorkspaceEntryPointKind.Project => "workspace/project",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The workspace entry-point kind is not defined."),
        };

    private static string ValueKindToken(WorkspaceSnapshotValueKind kind) =>
        kind switch
        {
            WorkspaceSnapshotValueKind.DotNetSdkIdentity =>
                "toolchain/dotnet-sdk",
            WorkspaceSnapshotValueKind.MsBuildIdentity =>
                "toolchain/msbuild",
            WorkspaceSnapshotValueKind.RoslynIdentity =>
                "toolchain/roslyn",
            WorkspaceSnapshotValueKind.Configuration =>
                "build/configuration",
            WorkspaceSnapshotValueKind.TargetFramework =>
                "build/target-framework",
            WorkspaceSnapshotValueKind.RuntimeIdentifier =>
                "build/runtime-identifier",
            WorkspaceSnapshotValueKind.ExplicitMsBuildProperty =>
                "build/explicit-property",
            WorkspaceSnapshotValueKind.SourceGeneratorIdentity =>
                "execution/source-generator",
            WorkspaceSnapshotValueKind.AnalyzerIdentity =>
                "execution/analyzer",
            WorkspaceSnapshotValueKind.GitWorktreeState =>
                "git/worktree-state",
            WorkspaceSnapshotValueKind.GitConflictState =>
                "git/conflict-state",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The workspace snapshot value kind is not defined."),
        };

    private sealed class FileEntry
    {
        public FileEntry(
            WorkspaceSnapshotFileInput input,
            string kindToken)
        {
            Input = input;
            KindToken = kindToken;
            KindTokenBytes = WorkspaceSnapshotEncoding.Encode(
                kindToken,
                nameof(kindToken));
        }

        public WorkspaceSnapshotFileInput Input { get; }

        public string KindToken { get; }

        public byte[] KindTokenBytes { get; }
    }

    private sealed class EntryPointEntry
    {
        public EntryPointEntry(
            WorkspaceSnapshotEntryPointInput input,
            string kindToken)
        {
            Input = input;
            KindTokenBytes = WorkspaceSnapshotEncoding.Encode(
                kindToken,
                nameof(kindToken));
        }

        public WorkspaceSnapshotEntryPointInput Input { get; }

        public byte[] KindTokenBytes { get; }
    }

    private sealed class ValueEntry
    {
        public ValueEntry(
            WorkspaceSnapshotValueInput input,
            string kindToken)
        {
            Input = input;
            KindToken = kindToken;
            KindTokenBytes = WorkspaceSnapshotEncoding.Encode(
                kindToken,
                nameof(kindToken));
        }

        public WorkspaceSnapshotValueInput Input { get; }

        public string KindToken { get; }

        public byte[] KindTokenBytes { get; }
    }
}
