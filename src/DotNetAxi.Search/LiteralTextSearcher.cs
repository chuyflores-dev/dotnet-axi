using DotNetAxi.Contracts;

namespace DotNetAxi.Search;

public sealed class LiteralTextSearcher : ILiteralTextSearcher
{
    private readonly IWorkspacePathTraverser _traverser;

    public LiteralTextSearcher(IWorkspacePathTraverser traverser) =>
        _traverser = traverser
            ?? throw new ArgumentNullException(nameof(traverser));

    public TextSearchResult Search(TextSearchRequest request) =>
        SearchAsync(request).GetAwaiter().GetResult();

    public Task<TextSearchResult> SearchAsync(
        TextSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var comparison = request.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return BuiltInTextSearchEngine.SearchAsync(
            _traverser,
            request,
            new LiteralTextMatcher(comparison, request.Query),
            cancellationToken);
    }
}
