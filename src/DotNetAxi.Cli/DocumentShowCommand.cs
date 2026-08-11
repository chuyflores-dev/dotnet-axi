using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DotNetAxi.Contracts;
using DotNetAxi.Search;
using DotNetAxi.Workspaces;

namespace DotNetAxi.Cli;

internal sealed record DocumentShowCommandRequest(
    string Path,
    bool IncludeGenerated,
    int MaxCharacters,
    bool MaxCharactersSpecified,
    int? StartLine,
    int? EndLine,
    bool Full)
{
    public static DocumentShowCommandRequest Create(
        string path,
        bool includeGenerated,
        int maxCharacters,
        bool maxCharactersSpecified,
        int? startLine,
        int? endLine,
        bool full)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CommandUsageException(
                "usage.document_path",
                "The document path cannot be blank.",
                "Provide one explicit document path.");
        }

        if (maxCharacters < 0)
        {
            throw new CommandUsageException(
                "usage.max_chars",
                "The --max-chars value cannot be negative.",
                "Use a non-negative --max-chars value.");
        }

        if (full && maxCharactersSpecified)
        {
            throw new CommandUsageException(
                "usage.document_budget",
                "The --full and --max-chars options cannot be combined.",
                "Use either --full or one explicit --max-chars value.");
        }

        if (startLine is <= 0)
        {
            throw new CommandUsageException(
                "usage.start_line",
                "The --start-line value must be positive.",
                "Use a positive, one-based --start-line value.");
        }

        if (endLine is <= 0)
        {
            throw new CommandUsageException(
                "usage.end_line",
                "The --end-line value must be positive.",
                "Use a positive, one-based --end-line value.");
        }

        if (startLine > endLine)
        {
            throw new CommandUsageException(
                "usage.document_line_span",
                "The --start-line value cannot be greater than --end-line.",
                "Use an inclusive line span whose start is not after its end.");
        }

        return new DocumentShowCommandRequest(
            path,
            includeGenerated,
            maxCharacters,
            maxCharactersSpecified,
            startLine,
            endLine,
            full);
    }
}

internal sealed class DocumentShowCommandHandler :
    ICommandHandler<DocumentShowCommandRequest>
{
    public async ValueTask<ICommandResult> HandleAsync(
        DocumentShowCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = new WorkspaceDiscoverer()
            .Discover(Directory.GetCurrentDirectory());
        var resolution = ResolvePath(workspace, request.Path);
        if (Directory.Exists(resolution.FullPath))
        {
            throw new CommandUsageException(
                "usage.document_path",
                $"Path `{resolution.Path}` identifies a directory, not a document.",
                "Provide one explicit document file path.");
        }

        if (!File.Exists(resolution.FullPath))
        {
            return Failure(
                "document.not_found",
                $"Document `{resolution.Path}` does not exist.",
                "Correct the path and run the command again.");
        }

        var traversal = new WorkspaceTraversalRequest(
            workspace.RootPath,
            explicitPaths: [request.Path],
            includeGenerated: true,
            currentDirectory: workspace.CurrentDirectory);
        var paths = new WorkspacePathTraverser()
            .Traverse(traversal, cancellationToken);
        var document = paths.Count == 1 ? paths[0] : null;
        if (document is null)
        {
            return Failure(
                "document.excluded",
                $"Document `{resolution.Path}` is excluded by workspace traversal policy.",
                "Remove the applicable exclusion or select an eligible document.");
        }

        var read = await new BoundedTextDocumentReader()
            .ReadAsync(
                document.FullPath,
                request.Full ? null : request.MaxCharacters,
                WorkspaceGeneratedCodeClassifier.MaximumHeaderCharacters,
                request.StartLine,
                request.EndLine,
                cancellationToken)
            .ConfigureAwait(false);
        if (read.Status is not TextDocumentReadStatus.Success)
        {
            return ReadFailure(document.RelativePath, read.Status);
        }

        if (request.StartLine > read.TotalLines
            || request.EndLine > read.TotalLines)
        {
            return Failure(
                "document.line_span_out_of_range",
                $"Document `{document.RelativePath}` has {read.TotalLines} line(s), "
                    + "and the requested line span exceeds that range.",
                $"Use --start-line and --end-line values from 1 through {read.TotalLines}.");
        }

        var currentPaths = new WorkspacePathTraverser()
            .Traverse(traversal, cancellationToken);
        var currentDocument = currentPaths.Count == 1 ? currentPaths[0] : null;
        if (currentDocument is null || !SameDocument(document, currentDocument))
        {
            return ChangedDuringRead(document.RelativePath);
        }

        document = currentDocument;
        var isGenerated = document.IsGenerated
            || WorkspaceGeneratedCodeClassifier.HasGeneratedHeader(read.Header!);
        if (isGenerated && !request.IncludeGenerated)
        {
            return Failure(
                "document.generated_excluded",
                $"Document `{document.RelativePath}` is generated and excluded by default.",
                GeneratedCorrection(request));
        }

        var ownership = new WorkspaceProjectOwnershipResolver(
            workspace.RootPath,
            workspace.Projects.Select(static project => project.Path));
        var owners = ownership
            .GetOwningProjects(document)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var snapshot = await CaptureSnapshotAsync(
                document,
                isGenerated,
                owners,
                read.ContentHash!,
                read.ByteCount,
                cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            return ChangedDuringRead(document.RelativePath);
        }

        var finalPaths = new WorkspacePathTraverser()
            .Traverse(traversal, cancellationToken);
        var finalDocument = finalPaths.Count == 1 ? finalPaths[0] : null;
        if (finalDocument is null || !SameDocument(document, finalDocument))
        {
            return ChangedDuringRead(document.RelativePath);
        }

        document = finalDocument;
        var outlineCommandPath = document.IsExternal
            ? request.Path
            : new WorkspacePathResolver(
                workspace.RootPath,
                workspace.CurrentDirectory)
                .ToInputPath(document.RelativePath);
        var payload = new DocumentShowPayload(
            FileEntityIdentity.Create(document),
            document.RelativePath,
            document.IsExternal,
            isGenerated,
            owners.Length,
            owners,
            read.Encoding!,
            read.HasByteOrderMark,
            read.ByteCount,
            read.TotalLines,
            new DocumentRequestedLineSpan(
                request.StartLine ?? 1,
                request.EndLine ?? read.TotalLines),
            new DocumentActualLineSpan(
                read.ActualStartLine,
                read.ActualEndLine),
            read.Preview!,
            read.IncludedCharacters,
            TotalKnown: true,
            read.TotalCharacters,
            read.OmittedCharacters,
            read.Truncated,
            read.Truncated ? RetrievalCommand(request) : null,
            OutlineReference(
                document.RelativePath,
                outlineCommandPath,
                isGenerated));
        var evidence = new Evidence(
            snapshot,
            EvidenceResolution.Text,
            new EvidenceCoverage(
                CoverageLevel.Complete,
                considered: 1,
                analyzed: 1,
                remaining: 0,
                excluded: 0,
                failed: 0),
            EvidenceConfidence.Verified,
            new EvidenceScope(
                workspace.RootPath,
                document.IsExternal
                    ? "one explicitly selected external document"
                    : "one explicitly selected workspace document",
                projects: owners));

        return CommandResult<DocumentShowPayload>.Success(
            "show document",
            payload,
            evidence);
    }

    private static WorkspacePathResolution ResolvePath(
        WorkspaceDiscoveryResult workspace,
        string path)
    {
        try
        {
            return new WorkspacePathResolver(
                workspace.RootPath,
                workspace.CurrentDirectory)
                .ResolveInput(path, WorkspacePathScope.Explicit);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException)
        {
            throw new CommandUsageException(
                "usage.document_path",
                $"Document path `{path}` is invalid.",
                "Provide one valid document file path.");
        }
    }

    private static bool SameDocument(
        WorkspaceTraversalPath before,
        WorkspaceTraversalPath after) =>
        string.Equals(
            Path.GetFullPath(before.FullPath),
            Path.GetFullPath(after.FullPath),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal)
        && string.Equals(
            before.RelativePath,
            after.RelativePath,
            StringComparison.Ordinal)
        && before.IsExternal == after.IsExternal
        && before.IsGenerated == after.IsGenerated;

    internal static async Task<string?> CaptureSnapshotAsync(
        WorkspaceTraversalPath document,
        bool isGenerated,
        IReadOnlyList<string> owners,
        string expectedContentHash,
        long expectedByteCount,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 64 * 1024;
        try
        {
            await using var stream = new FileStream(
                document.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != expectedByteCount)
            {
                return null;
            }

            using var observation = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
            using var content = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
            Append(
                observation,
                "dotnet-axi/document-show-observation/v1",
                cancellationToken);
            Append(observation, document.RelativePath, cancellationToken);
            Append(
                observation,
                document.IsExternal ? "external" : "workspace",
                cancellationToken);
            Append(
                observation,
                isGenerated ? "generated" : "source",
                cancellationToken);
            Append(
                observation,
                owners.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                cancellationToken);
            foreach (var owner in owners)
            {
                Append(observation, owner, cancellationToken);
            }

            AppendLength(observation, expectedByteCount);
            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            long byteCount = 0;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = await stream.ReadAsync(
                            buffer.AsMemory(0, bufferSize),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (count == 0)
                    {
                        break;
                    }

                    byteCount += count;
                    var bytes = buffer.AsSpan(0, count);
                    content.AppendData(bytes);
                    observation.AppendData(bytes);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }

            if (byteCount != expectedByteCount
                || !string.Equals(
                    Convert.ToHexStringLower(content.GetHashAndReset()),
                    expectedContentHash,
                    StringComparison.Ordinal))
            {
                return null;
            }

            return "ws_" + Convert.ToHexStringLower(
                observation.GetHashAndReset());
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void AppendLength(IncrementalHash hash, long length)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, checked((ulong)length));
        hash.AppendData(bytes);
    }

    private static void Append(
        IncrementalHash hash,
        string value,
        CancellationToken cancellationToken) =>
        Append(hash, Encoding.UTF8.GetBytes(value), cancellationToken);

    private static void Append(
        IncrementalHash hash,
        ReadOnlySpan<byte> value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Span<byte> length = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(
            length,
            checked((ulong)value.Length));
        hash.AppendData(length);
        const int chunkSize = 64 * 1024;
        for (var offset = 0; offset < value.Length; offset += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(value.Slice(
                offset,
                Math.Min(chunkSize, value.Length - offset)));
        }
    }

    private static CommandResult<DocumentShowPayload> ReadFailure(
        string path,
        TextDocumentReadStatus status) =>
        status switch
        {
            TextDocumentReadStatus.Binary => Failure(
                "document.binary",
                $"Document `{path}` contains binary data.",
                "Select a supported UTF-8 or UTF-16 text document."),
            TextDocumentReadStatus.Undecodable => Failure(
                "document.undecodable",
                $"Document `{path}` cannot be decoded without data loss.",
                "Correct the document encoding or convert it to valid UTF-8 or UTF-16."),
            TextDocumentReadStatus.UnsupportedEncoding => Failure(
                "document.encoding_unsupported",
                $"Document `{path}` uses an unsupported encoding.",
                "Convert the document to UTF-8 or UTF-16 and run the command again."),
            TextDocumentReadStatus.Unreadable => Failure(
                "document.unreadable",
                $"Document `{path}` could not be read.",
                "Check file permissions and availability, then run the command again."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "The document read status does not describe a failure."),
        };

    private static CommandResult<DocumentShowPayload> ChangedDuringRead(
        string path) =>
        Failure(
            "document.changed_during_read",
            $"Document `{path}` changed while it was being read.",
            "Run the command again against a stable document path.");

    private static CommandResult<DocumentShowPayload> Failure(
        string code,
        string message,
        string correction) =>
        CommandResult<DocumentShowPayload>.Failed(
            "show document",
            [new ResultError(code, message, correction)]);

    private static string RetrievalCommand(DocumentShowCommandRequest request) =>
        CanonicalInvocation.OneShot(
            "dnaxi show document "
            + Quote(request.Path)
            + SpanArguments(request)
            + (request.IncludeGenerated ? " --include-generated" : string.Empty)
            + " --full");

    private static string GeneratedCorrection(
        DocumentShowCommandRequest request)
    {
        var invocation = CanonicalInvocation.OneShot(
            "dnaxi show document "
            + Quote(request.Path)
            + SpanArguments(request)
            + " --include-generated"
            + (request.Full
                ? " --full"
                : request.MaxCharactersSpecified
                    ? " --max-chars "
                        + request.MaxCharacters.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                    : string.Empty));
        return $"Run `{invocation}` to include it explicitly.";
    }

    private static DocumentOutlineReference OutlineReference(
        string path,
        string commandPath,
        bool generated)
    {
        var available = Path.GetExtension(path).Equals(
            ".cs",
            StringComparison.OrdinalIgnoreCase);
        return new DocumentOutlineReference(
            path,
            available,
            available
                ? CanonicalInvocation.OneShot(
                    "dnaxi outline "
                    + Quote(commandPath)
                    + (generated ? " --include-generated" : string.Empty))
                : null);
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string SpanArguments(DocumentShowCommandRequest request) =>
        (request.StartLine is { } startLine
            ? " --start-line "
                + startLine.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty)
        + (request.EndLine is { } endLine
            ? " --end-line "
                + endLine.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty);

    private sealed record DocumentShowPayload(
        string Id,
        string Path,
        bool External,
        bool Generated,
        int OwningProjectCount,
        IReadOnlyList<string> OwningProjects,
        string Encoding,
        bool ByteOrderMark,
        long ByteCount,
        long LineCount,
        DocumentRequestedLineSpan RequestedSpan,
        DocumentActualLineSpan ActualSpan,
        string Preview,
        long IncludedCharacters,
        bool TotalKnown,
        long TotalCharacters,
        long OmittedCharacters,
        bool Truncated,
        string? RetrievalCommand,
        DocumentOutlineReference OutlineReference);

    private sealed record DocumentRequestedLineSpan(
        long StartLine,
        long EndLine);

    private sealed record DocumentActualLineSpan(
        long? StartLine,
        long? EndLine);

    private sealed record DocumentOutlineReference(
        string Path,
        bool Available,
        string? Command);
}
