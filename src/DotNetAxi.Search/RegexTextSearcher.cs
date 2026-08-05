using System.Text.RegularExpressions;
using DotNetAxi.Contracts;

namespace DotNetAxi.Search;

public sealed class RegexTextSearcher : IRegexTextSearcher
{
    private readonly IWorkspacePathTraverser _traverser;

    public RegexTextSearcher(IWorkspacePathTraverser traverser) =>
        _traverser = traverser
            ?? throw new ArgumentNullException(nameof(traverser));

    public TextSearchResult Search(RegexTextSearchRequest request) =>
        SearchAsync(request).GetAwaiter().GetResult();

    public Task<TextSearchResult> SearchAsync(
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
            return Task.FromResult(
                BuiltInTextSearchEngine.InvalidRegularExpression(request));
        }

        return BuiltInTextSearchEngine.SearchAsync(
            _traverser,
            request,
            new RegexTextMatcher(
                request.Query,
                options,
                request.PerFileTimeout),
            cancellationToken);
    }
}
