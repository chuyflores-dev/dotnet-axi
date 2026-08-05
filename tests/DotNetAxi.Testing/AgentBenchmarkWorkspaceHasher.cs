using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace DotNetAxi.Testing;

internal static class AgentBenchmarkWorkspaceHasher
{
    private const int MaximumEntries = 10_000;

    private static readonly byte[] Domain =
        "dotnet-axi/agent-benchmark-workspace/v1\n"u8.ToArray();

    public static async ValueTask<AgentBenchmarkWorkspaceBaseline>
        CaptureBaselineAsync(
            string workspacePath,
            IReadOnlyList<string> declaredFiles,
            TimeSpan timeout,
            CancellationToken cancellationToken)
    {
        var inventory = await InspectInventoryAsync(
            workspacePath,
            timeout,
            cancellationToken);
        if (!inventory.Complete || inventory.RootIsReparsePoint)
        {
            throw new AgentBenchmarkException(
                "The initial benchmark workspace could not be inspected safely.");
        }

        var declared = new Dictionary<string, DeclaredFileBaseline>(
            StringComparer.Ordinal);
        foreach (var relativePath in declaredFiles)
        {
            if (!inventory.Entries.TryGetValue(relativePath, out var entry)
                || entry.Kind != WorkspaceEntryKind.RegularFile)
            {
                throw new AgentBenchmarkException(
                    $"Declared fixture file '{relativePath}' is not a regular file.");
            }

            var contentHash = await ReadDeclaredContentHashAsync(
                workspacePath,
                relativePath,
                entry.Length,
                timeout,
                cancellationToken);
            declared.Add(
                relativePath,
                new DeclaredFileBaseline(entry.Length, contentHash));
        }

        return new AgentBenchmarkWorkspaceBaseline(
            inventory.Hash,
            new ReadOnlyDictionary<string, DeclaredFileBaseline>(declared));
    }

    public static async ValueTask<AgentBenchmarkWorkspaceInspection>
        InspectAsync(
            string workspacePath,
            AgentBenchmarkWorkspaceBaseline baseline,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
    {
        try
        {
            var inventory = await InspectInventoryAsync(
                workspacePath,
                timeout,
                cancellationToken);
            if (!inventory.Complete)
            {
                return new AgentBenchmarkWorkspaceInspection(
                    inventory.Hash,
                    Complete: false,
                    MatchesBaseline: false,
                    inventory.Detail);
            }

            var matches = string.Equals(
                inventory.Hash,
                baseline.InventoryHash,
                StringComparison.Ordinal);
            if (!matches)
            {
                return new AgentBenchmarkWorkspaceInspection(
                    inventory.Hash,
                    Complete: true,
                    MatchesBaseline: false,
                    "The workspace inventory differs from the initial state.");
            }

            foreach (var declared in baseline.DeclaredFiles)
            {
                if (!inventory.Entries.TryGetValue(
                        declared.Key,
                        out var entry)
                    || entry.Kind != WorkspaceEntryKind.RegularFile
                    || entry.Length != declared.Value.Length)
                {
                    matches = false;
                    continue;
                }

                var contentHash = await ReadDeclaredContentHashAsync(
                    workspacePath,
                    declared.Key,
                    entry.Length,
                    timeout,
                    cancellationToken);
                if (!string.Equals(
                        contentHash,
                        declared.Value.ContentHash,
                        StringComparison.Ordinal))
                {
                    matches = false;
                }
            }

            return new AgentBenchmarkWorkspaceInspection(
                inventory.Hash,
                Complete: true,
                matches,
                matches
                    ? "The complete workspace inventory and declared content match the initial state."
                    : "The workspace inventory or declared content differs from the initial state.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException
                  or OperationCanceledException)
        {
            return FailedInspection(exception);
        }
    }

    private static async ValueTask<WorkspaceInventory> InspectInventoryAsync(
        string workspacePath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var inspection = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        inspection.CancelAfter(timeout);
        try
        {
            var rootPath = Path.GetFullPath(workspacePath);
            var entries = new Dictionary<string, WorkspaceEntry>(
                StringComparer.Ordinal);
            var rootAttributes = File.GetAttributes(rootPath);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                var root = ReparseEntry(
                    ".",
                    rootPath,
                    rootAttributes,
                    isRoot: true);
                entries.Add(root.RelativePath, root);
                return Inventory(
                    entries,
                    complete: true,
                    rootIsReparsePoint: true,
                    "The workspace root is a reparse point and was not traversed.");
            }

            if ((rootAttributes & FileAttributes.Directory) == 0)
            {
                return Inventory(
                    entries,
                    complete: false,
                    rootIsReparsePoint: false,
                    "The workspace root is not a directory.");
            }

            entries.Add(
                ".",
                new WorkspaceEntry(
                    ".",
                    WorkspaceEntryKind.RootDirectory,
                    Length: 0,
                    LastWriteTicks: 0,
                    LinkTarget: string.Empty));
            await CollectAsync(
                rootPath,
                string.Empty,
                entries,
                inspection.Token);
            return Inventory(
                entries,
                complete: true,
                rootIsReparsePoint: false,
                "Workspace inspection completed.");
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException
                  or OperationCanceledException)
        {
            return new WorkspaceInventory(
                AgentBenchmarkHash.Compute(
                    $"workspace-inspection-failed:{exception.GetType().Name}"),
                new ReadOnlyDictionary<string, WorkspaceEntry>(
                    new Dictionary<string, WorkspaceEntry>(
                        StringComparer.Ordinal)),
                Complete: false,
                RootIsReparsePoint: false,
                $"Workspace inspection failed with {exception.GetType().Name}.");
        }
    }

    private static async ValueTask CollectAsync(
        string directoryPath,
        string relativeDirectory,
        IDictionary<string, WorkspaceEntry> entries,
        CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directoryPath)
                     .Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries.Count >= MaximumEntries)
            {
                throw new IOException(
                    $"Workspace inventory exceeds {MaximumEntries} entries.");
            }

            var name = Path.GetFileName(path);
            var relativePath = relativeDirectory.Length == 0
                ? name
                : $"{relativeDirectory}/{name}";
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                entries.Add(
                    relativePath,
                    ReparseEntry(
                        relativePath,
                        path,
                        attributes,
                        isRoot: false));
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                entries.Add(
                    relativePath,
                    new WorkspaceEntry(
                        relativePath,
                        WorkspaceEntryKind.Directory,
                        Length: 0,
                        LastWriteTicks: 0,
                        LinkTarget: string.Empty));
                await CollectAsync(
                    path,
                    relativePath,
                    entries,
                    cancellationToken);
                continue;
            }

            var kind = (attributes & FileAttributes.Device) != 0
                ? WorkspaceEntryKind.Special
                : WorkspaceEntryKind.RegularFile;
            var length = kind == WorkspaceEntryKind.RegularFile
                ? new FileInfo(path).Length
                : 0;
            var lastWriteTicks = kind == WorkspaceEntryKind.RegularFile
                ? new FileInfo(path).LastWriteTimeUtc.Ticks
                : 0;
            entries.Add(
                relativePath,
                new WorkspaceEntry(
                    relativePath,
                    kind,
                    length,
                    lastWriteTicks,
                    LinkTarget: string.Empty));
        }
    }

    private static WorkspaceEntry ReparseEntry(
        string relativePath,
        string path,
        FileAttributes attributes,
        bool isRoot)
    {
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var target = isDirectory
            ? new DirectoryInfo(path).LinkTarget
            : new FileInfo(path).LinkTarget;
        return new WorkspaceEntry(
            relativePath,
            isRoot
                ? WorkspaceEntryKind.RootReparsePoint
                : isDirectory
                    ? WorkspaceEntryKind.DirectoryReparsePoint
                    : WorkspaceEntryKind.FileReparsePoint,
            Length: 0,
            LastWriteTicks: 0,
            target ?? string.Empty);
    }

    private static WorkspaceInventory Inventory(
        IDictionary<string, WorkspaceEntry> entries,
        bool complete,
        bool rootIsReparsePoint,
        string detail)
    {
        var snapshot = new ReadOnlyDictionary<string, WorkspaceEntry>(
            new Dictionary<string, WorkspaceEntry>(
                entries,
                StringComparer.Ordinal));
        return new WorkspaceInventory(
            Hash(snapshot.Values),
            snapshot,
            complete,
            rootIsReparsePoint,
            detail);
    }

    private static string Hash(IEnumerable<WorkspaceEntry> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain);
        Span<byte> length = stackalloc byte[sizeof(long)];
        foreach (var entry in entries.OrderBy(
                     static entry => entry.RelativePath,
                     StringComparer.Ordinal))
        {
            AppendField(hash, Encoding.UTF8.GetBytes(entry.Kind.ToString()));
            AppendField(hash, Encoding.UTF8.GetBytes(entry.RelativePath));
            BinaryPrimitives.WriteInt64BigEndian(length, entry.Length);
            hash.AppendData(length);
            BinaryPrimitives.WriteInt64BigEndian(
                length,
                entry.LastWriteTicks);
            hash.AppendData(length);
            AppendField(hash, Encoding.UTF8.GetBytes(entry.LinkTarget));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async ValueTask<string> ReadDeclaredContentHashAsync(
        string workspacePath,
        string relativePath,
        long expectedLength,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var read = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        read.CancelAfter(timeout);
        var path = Path.Combine(
            workspacePath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != expectedLength)
        {
            throw new IOException(
                $"Declared file '{relativePath}' changed length during inspection.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[16 * 1024];
        long readLength = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer, read.Token);
            if (count == 0)
            {
                break;
            }

            readLength += count;
            if (readLength > expectedLength)
            {
                throw new IOException(
                    $"Declared file '{relativePath}' grew during inspection.");
            }

            hash.AppendData(buffer.AsSpan(0, count));
        }

        if (readLength != expectedLength)
        {
            throw new IOException(
                $"Declared file '{relativePath}' changed during inspection.");
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static AgentBenchmarkWorkspaceInspection FailedInspection(
        Exception exception) =>
        new(
            AgentBenchmarkHash.Compute(
                $"workspace-inspection-failed:{exception.GetType().Name}"),
            Complete: false,
            MatchesBaseline: false,
            $"Workspace inspection failed with {exception.GetType().Name}.");

    private static void AppendField(IncrementalHash hash, byte[] value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }

    private enum WorkspaceEntryKind
    {
        RootDirectory,
        RootReparsePoint,
        Directory,
        RegularFile,
        FileReparsePoint,
        DirectoryReparsePoint,
        Special,
    }

    private sealed record WorkspaceEntry(
        string RelativePath,
        WorkspaceEntryKind Kind,
        long Length,
        long LastWriteTicks,
        string LinkTarget);

    private sealed record WorkspaceInventory(
        string Hash,
        IReadOnlyDictionary<string, WorkspaceEntry> Entries,
        bool Complete,
        bool RootIsReparsePoint,
        string Detail);
}

internal sealed record DeclaredFileBaseline(
    long Length,
    string ContentHash);

internal sealed record AgentBenchmarkWorkspaceBaseline(
    string InventoryHash,
    IReadOnlyDictionary<string, DeclaredFileBaseline> DeclaredFiles);

internal sealed record AgentBenchmarkWorkspaceInspection(
    string Hash,
    bool Complete,
    bool MatchesBaseline,
    string Detail);
