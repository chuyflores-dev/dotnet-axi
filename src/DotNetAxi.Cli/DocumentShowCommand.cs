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
    bool Full)
{
    public int EffectiveMaxCharacters => Full ? int.MaxValue : MaxCharacters;

    public static DocumentShowCommandRequest Create(
        string path,
        bool includeGenerated,
        int maxCharacters,
        bool maxCharactersSpecified,
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

        return new DocumentShowCommandRequest(
            path,
            includeGenerated,
            maxCharacters,
            maxCharactersSpecified,
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

        var read = await new TextDocumentReader()
            .ReadAsync(document.FullPath, cancellationToken)
            .ConfigureAwait(false);
        if (read.Status is not TextDocumentReadStatus.Success)
        {
            return ReadFailure(document.RelativePath, read.Status);
        }

        var currentPaths = new WorkspacePathTraverser()
            .Traverse(traversal, cancellationToken);
        var currentDocument = currentPaths.Count == 1 ? currentPaths[0] : null;
        if (currentDocument is null || !SameDocument(document, currentDocument))
        {
            return Failure(
                "document.changed_during_read",
                $"Document `{document.RelativePath}` changed while it was being read.",
                "Run the command again against a stable document path.");
        }

        var isGenerated = document.IsGenerated
            || WorkspaceGeneratedCodeClassifier.HasGeneratedHeader(read.Text!);
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
        var snapshot = SnapshotIdentity(
            document,
            isGenerated,
            read.Content.Span,
            owners,
            cancellationToken);
        var retrievalCommand = RetrievalCommand(request);
        var bounded = BoundedText.Create(
            read.Text!,
            request.EffectiveMaxCharacters,
            retrievalCommand);
        var payload = new DocumentShowPayload(
            FileEntityIdentity.Create(document),
            document.RelativePath,
            document.IsExternal,
            isGenerated,
            owners.Length,
            owners,
            read.Encoding!,
            read.HasByteOrderMark,
            read.Content.Length,
            bounded.Preview,
            bounded.IncludedCharacters,
            TotalKnown: true,
            bounded.TotalCharacters,
            bounded.OmittedCharacters,
            bounded.Truncated,
            bounded.RetrievalCommand,
            new DocumentOutlineReference(
                document.RelativePath,
                Available: false));
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

    private static string SnapshotIdentity(
        WorkspaceTraversalPath document,
        bool isGenerated,
        ReadOnlySpan<byte> content,
        IReadOnlyList<string> owners,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/document-show-observation/v1", cancellationToken);
        Append(hash, document.RelativePath, cancellationToken);
        Append(
            hash,
            document.IsExternal ? "external" : "workspace",
            cancellationToken);
        Append(hash, isGenerated ? "generated" : "source", cancellationToken);
        Append(
            hash,
            owners.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            cancellationToken);
        foreach (var owner in owners)
        {
            Append(hash, owner, cancellationToken);
        }

        Append(hash, content, cancellationToken);
        return "ws_" + Convert.ToHexStringLower(hash.GetHashAndReset());
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
            + (request.IncludeGenerated ? " --include-generated" : string.Empty)
            + " --full");

    private static string GeneratedCorrection(
        DocumentShowCommandRequest request)
    {
        var invocation = CanonicalInvocation.OneShot(
            "dnaxi show document "
            + Quote(request.Path)
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

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private sealed record DocumentShowPayload(
        string Id,
        string Path,
        bool External,
        bool Generated,
        int OwningProjectCount,
        IReadOnlyList<string> OwningProjects,
        string Encoding,
        bool ByteOrderMark,
        int ByteCount,
        string Preview,
        int IncludedCharacters,
        bool TotalKnown,
        int TotalCharacters,
        int OmittedCharacters,
        bool Truncated,
        string? RetrievalCommand,
        DocumentOutlineReference OutlineReference);

    private sealed record DocumentOutlineReference(
        string Path,
        bool Available);
}
