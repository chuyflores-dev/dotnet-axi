using System.Text.RegularExpressions;
using DotNetAxi.Contracts;

namespace DotNetAxi.Search;

public sealed class RegexTextSearcher : IRegexTextSearcher
{
    private readonly IWorkspacePathTraverser _traverser;
    private readonly RgTextSearchAccelerator? _accelerator;

    public RegexTextSearcher(IWorkspacePathTraverser traverser) =>
        _traverser = traverser
            ?? throw new ArgumentNullException(nameof(traverser));

    public RegexTextSearcher(
        IWorkspacePathTraverser traverser,
        RgTextSearchAccelerator accelerator)
    {
        _traverser = traverser
            ?? throw new ArgumentNullException(nameof(traverser));
        _accelerator = accelerator
            ?? throw new ArgumentNullException(nameof(accelerator));
    }

    public TextSearchResult Search(RegexTextSearchRequest request) =>
        SearchAsync(request).GetAwaiter().GetResult();

    public async Task<TextSearchResult> SearchAsync(
        RegexTextSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var options = RegexOptions.CultureInvariant;
        if (!request.CaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        try
        {
            _ = new Regex(request.Query, options, request.PerFileTimeout);
        }
        catch (RegexParseException)
        {
            return BuiltInTextSearchEngine.InvalidRegularExpression(request);
        }

        var matcher = new RegexTextMatcher(
            request.Query,
            options,
            request.PerFileTimeout);
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
