using DotNetAxi.Contracts;

namespace DotNetAxi.Search;

public sealed class LiteralTextSearcher : ILiteralTextSearcher
{
    private readonly IWorkspacePathTraverser _traverser;
    private readonly RgTextSearchAccelerator? _accelerator;

    public LiteralTextSearcher(IWorkspacePathTraverser traverser) =>
        _traverser = traverser
            ?? throw new ArgumentNullException(nameof(traverser));

    public LiteralTextSearcher(
        IWorkspacePathTraverser traverser,
        RgTextSearchAccelerator accelerator)
    {
        _traverser = traverser
            ?? throw new ArgumentNullException(nameof(traverser));
        _accelerator = accelerator
            ?? throw new ArgumentNullException(nameof(accelerator));
    }

    public TextSearchResult Search(TextSearchRequest request) =>
        SearchAsync(request).GetAwaiter().GetResult();

    public async Task<TextSearchResult> SearchAsync(
        TextSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var comparison = request.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var matcher = new LiteralTextMatcher(comparison, request.Query);
        if (_accelerator is null)
        {
            return await BuiltInTextSearchEngine.SearchAsync(
                    _traverser,
                    request,
                    matcher,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var paths = _traverser.Traverse(request.Traversal, cancellationToken);
        var candidatePaths = await _accelerator.FindCandidatePathsAsync(
                request,
                matcher,
                paths,
                cancellationToken)
            .ConfigureAwait(false);
        return await BuiltInTextSearchEngine.SearchAsync(
                paths,
                request,
                matcher,
                candidatePaths,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
