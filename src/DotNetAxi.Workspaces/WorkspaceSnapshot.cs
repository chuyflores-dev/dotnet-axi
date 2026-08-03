using System.Text;

namespace DotNetAxi.Workspaces;

public enum WorkspaceSnapshotFileKind
{
    SelectedSource,
    LinkedSource,
    AdditionalFile,
    AnalyzerConfiguration,
    Solution,
    Project,
    MsBuildImport,
    GlobalJson,
    NuGetAssets,
    NuGetLock,
    GeneratedSourceInput,
}

public enum WorkspaceSnapshotValueKind
{
    DotNetSdkIdentity,
    MsBuildIdentity,
    RoslynIdentity,
    Configuration,
    TargetFramework,
    RuntimeIdentifier,
    ExplicitMsBuildProperty,
    SourceGeneratorIdentity,
    AnalyzerIdentity,
    GitWorktreeState,
    GitConflictState,
}

public sealed class WorkspaceSnapshotFileInput
{
    private readonly byte[] _content;
    private readonly byte[] _pathBytes;

    public WorkspaceSnapshotFileInput(
        WorkspaceSnapshotFileKind kind,
        string path,
        ReadOnlyMemory<byte> content)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The workspace snapshot file kind is not defined.");
        }

        Kind = kind;
        Path = WorkspaceSnapshotEncoding.NormalizePath(path, nameof(path));
        _pathBytes = WorkspaceSnapshotEncoding.Encode(Path, nameof(path));
        _content = content.ToArray();
    }

    public WorkspaceSnapshotFileKind Kind { get; }

    public string Path { get; }

    internal ReadOnlySpan<byte> Content => _content;

    internal ReadOnlySpan<byte> PathBytes => _pathBytes;
}

public sealed class WorkspaceSnapshotValueInput
{
    private readonly byte[] _nameBytes;
    private readonly byte[]? _scopePathBytes;
    private readonly byte[] _valueBytes;

    public WorkspaceSnapshotValueInput(
        WorkspaceSnapshotValueKind kind,
        string name,
        string value,
        string? scopePath = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The workspace snapshot value kind is not defined.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        Kind = kind;
        Name = name;
        ScopePath = scopePath is null
            ? null
            : WorkspaceSnapshotEncoding.NormalizePath(
                scopePath,
                nameof(scopePath));
        _nameBytes = WorkspaceSnapshotEncoding.Encode(name, nameof(name));
        _scopePathBytes = ScopePath is null
            ? null
            : WorkspaceSnapshotEncoding.Encode(
                ScopePath,
                nameof(scopePath));
        _valueBytes = WorkspaceSnapshotEncoding.Encode(value, nameof(value));
    }

    public WorkspaceSnapshotValueKind Kind { get; }

    public string Name { get; }

    public string? ScopePath { get; }

    internal ReadOnlySpan<byte> NameBytes => _nameBytes;

    internal ReadOnlySpan<byte> ScopePathBytes => _scopePathBytes;

    internal ReadOnlySpan<byte> ValueBytes => _valueBytes;
}

public sealed class WorkspaceSnapshotCapture
{
    public WorkspaceSnapshotCapture(
        IEnumerable<WorkspaceSnapshotFileInput> files,
        IEnumerable<WorkspaceSnapshotValueInput> values)
    {
        Files = Copy(files, nameof(files));
        Values = Copy(values, nameof(values));
    }

    public IReadOnlyList<WorkspaceSnapshotFileInput> Files { get; }

    public IReadOnlyList<WorkspaceSnapshotValueInput> Values { get; }

    private static IReadOnlyList<T> Copy<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Snapshot inputs cannot contain null entries.",
                parameterName);
        }

        return Array.AsReadOnly(copy);
    }
}

public sealed record WorkspaceSnapshotFileScope(
    WorkspaceSnapshotFileKind Kind,
    string Path,
    string ContentHash);

public sealed record WorkspaceSnapshotValueScope(
    WorkspaceSnapshotValueKind Kind,
    string Name,
    string? ScopePath,
    string ContentHash);

public sealed class WorkspaceSnapshotScope
{
    internal WorkspaceSnapshotScope(
        IEnumerable<WorkspaceSnapshotFileScope> files,
        IEnumerable<WorkspaceSnapshotValueScope> values)
    {
        Files = Array.AsReadOnly(files.ToArray());
        Values = Array.AsReadOnly(values.ToArray());
    }

    public IReadOnlyList<WorkspaceSnapshotFileScope> Files { get; }

    public IReadOnlyList<WorkspaceSnapshotValueScope> Values { get; }
}

public sealed class WorkspaceSnapshot
{
    internal WorkspaceSnapshot(
        string identity,
        WorkspaceSnapshotScope scope)
    {
        Identity = identity;
        Scope = scope;
    }

    public string Identity { get; }

    public WorkspaceSnapshotScope Scope { get; }
}

internal static class WorkspaceSnapshotEncoding
{
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] Encode(string value, string parameterName)
    {
        try
        {
            return StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Snapshot text must contain valid Unicode scalar values.",
                parameterName,
                exception);
        }
    }

    public static string NormalizePath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        return path.Replace('\\', '/');
    }
}
