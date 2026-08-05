using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DotNetAxi.Contracts;

namespace DotNetAxi.Search;

public sealed class FileSearchGlobException : ArgumentException
{
    internal FileSearchGlobException(
        string pattern,
        Exception innerException)
        : base("The file-search glob is invalid.", nameof(pattern), innerException)
    {
        Pattern = pattern;
    }

    public string Pattern { get; }
}

public sealed class FileSearcher : IFileSearcher
{
    private readonly IWorkspacePathTraverser _traverser;
    private readonly IFileOwnershipResolver _ownershipResolver;

    public FileSearcher(
        IWorkspacePathTraverser traverser,
        IFileOwnershipResolver ownershipResolver)
    {
        _traverser = traverser
            ?? throw new ArgumentNullException(nameof(traverser));
        _ownershipResolver = ownershipResolver
            ?? throw new ArgumentNullException(nameof(ownershipResolver));
    }

    public FileSearchResult Search(
        FileSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var comparison = request.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var extensionComparer = request.CaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        var extensions = request.Extensions.ToHashSet(extensionComparer);
        var globs = request.Globs
            .Select(pattern => FilePathGlob.Parse(
                pattern,
                ignoreCase: !request.CaseSensitive))
            .ToArray();
        var paths = _traverser
            .Traverse(request.Traversal, cancellationToken)
            .OrderBy(static path => path.RelativePath, StringComparer.Ordinal)
            .ThenBy(static path => path.FullPath, StringComparer.Ordinal)
            .GroupBy(static path => path.RelativePath, StringComparer.Ordinal)
            .Select(static paths => paths.First())
            .ToArray();
        var rankedMatches = new List<RankedMatch>();

        using var observation = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        Append(observation, "dotnet-axi/file-search-observation/v1");
        Append(observation, request.Query);
        Append(
            observation,
            request.CaseSensitive ? "sensitive" : "insensitive");
        AppendValues(observation, request.Extensions);
        AppendValues(observation, request.Globs);

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owners = _ownershipResolver
                .GetOwningProjects(path)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            Append(observation, path.RelativePath);
            Append(observation, path.IsExternal ? "external" : "workspace");
            AppendValues(observation, owners);

            if (!MatchesQuery(path.RelativePath, request.Query, comparison)
                || !MatchesExtension(
                    path.RelativePath,
                    extensions)
                || !MatchesGlob(path.RelativePath, globs))
            {
                continue;
            }

            var match = new FileSearchMatch(
                CreateId(path),
                path.RelativePath,
                FileKind(path.RelativePath),
                path.IsExternal,
                owners);
            rankedMatches.Add(new RankedMatch(
                match,
                Rank(path.RelativePath, request.Query, comparison)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ordered = rankedMatches
            .OrderBy(static item => item.Rank.Tier)
            .ThenBy(static item => item.Rank.MatchIndex)
            .ThenBy(static item => item.Rank.FileNameLength)
            .ThenBy(static item => item.Rank.PathDepth)
            .ThenBy(static item => item.Match.Path.Length)
            .ThenBy(static item => item.Match.Path, StringComparer.Ordinal)
            .Select(static item => item.Match)
            .ToArray();
        var limited = ordered.Take(request.Limit).ToArray();

        return new FileSearchResult(
            limited,
            ordered.Length,
            "ws_" + Convert.ToHexStringLower(
                observation.GetHashAndReset()));
    }

    private static bool MatchesQuery(
        string path,
        string query,
        StringComparison comparison) =>
        path.Contains(query, comparison);

    private static bool MatchesExtension(
        string path,
        IReadOnlySet<string> extensions)
    {
        if (extensions.Count == 0)
        {
            return true;
        }

        var extension = Path.GetExtension(path).TrimStart('.');
        return extensions.Contains(extension);
    }

    private static bool MatchesGlob(
        string path,
        IReadOnlyList<FilePathGlob> globs) =>
        globs.Count == 0 || globs.Any(glob => glob.Matches(path));

    private static FileRank Rank(
        string path,
        string query,
        StringComparison comparison)
    {
        var fileName = FileName(path);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var fileNameIndex = fileName.IndexOf(query, comparison);
        var pathIndex = path.IndexOf(query, comparison);
        int tier;

        if (path.Equals(query, comparison))
        {
            tier = 0;
        }
        else if (fileName.Equals(query, comparison))
        {
            tier = 1;
        }
        else if (nameWithoutExtension.Equals(query, comparison))
        {
            tier = 2;
        }
        else if (fileName.StartsWith(query, comparison))
        {
            tier = 3;
        }
        else if (fileNameIndex >= 0)
        {
            tier = 4;
        }
        else if (DirectorySegments(path).Any(segment =>
                     segment.Equals(query, comparison)))
        {
            tier = 5;
        }
        else if (DirectorySegments(path).Any(segment =>
                     segment.StartsWith(query, comparison)))
        {
            tier = 6;
        }
        else
        {
            tier = 7;
        }

        return new FileRank(
            tier,
            fileNameIndex >= 0 ? fileNameIndex : pathIndex,
            fileName.Length,
            path.Count(static character => character == '/'));
    }

    private static string FileName(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    private static IEnumerable<string> DirectorySegments(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0
            ? []
            : path[..separator].Split('/');
    }

    private static string FileKind(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "source",
            ".csproj" => "project",
            ".sln" or ".slnx" => "solution",
            ".props" or ".targets" => "build",
            _ => "file",
        };

    private static string CreateId(WorkspaceTraversalPath path)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/file-match/v1");
        Append(hash, path.RelativePath);
        Append(hash, path.IsExternal ? "external" : "workspace");
        return "file/v1/" + Convert.ToHexStringLower(
            hash.GetHashAndReset());
    }

    private static void AppendValues(
        IncrementalHash hash,
        IReadOnlyCollection<string> values)
    {
        Append(
            hash,
            values.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var value in values)
        {
            Append(hash, value);
        }
    }

    private static void Append(IncrementalHash hash, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private sealed record RankedMatch(
        FileSearchMatch Match,
        FileRank Rank);

    private sealed record FileRank(
        int Tier,
        int MatchIndex,
        int FileNameLength,
        int PathDepth);

    private sealed class FilePathGlob
    {
        private readonly Regex _expression;
        private readonly bool _matchesAnySegment;

        private FilePathGlob(Regex expression, bool matchesAnySegment)
        {
            _expression = expression;
            _matchesAnySegment = matchesAnySegment;
        }

        public static FilePathGlob Parse(string pattern, bool ignoreCase)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
            var normalized = pattern.Replace('\\', '/');
            try
            {
                return new FilePathGlob(
                    new Regex(
                        "\\A" + ToRegularExpression(normalized) + "\\z",
                        RegexOptions.CultureInvariant
                        | RegexOptions.ExplicitCapture
                        | RegexOptions.NonBacktracking
                        | (ignoreCase
                            ? RegexOptions.IgnoreCase
                            : RegexOptions.None)),
                    matchesAnySegment: !normalized.Contains('/'));
            }
            catch (ArgumentException exception)
            {
                throw new FileSearchGlobException(pattern, exception);
            }
        }

        public bool Matches(string path) =>
            _matchesAnySegment
                ? path.Split('/').Any(segment =>
                    _expression.IsMatch(segment))
                : _expression.IsMatch(path);

        private static string ToRegularExpression(string pattern)
        {
            var expression = new StringBuilder();
            for (var index = 0; index < pattern.Length; index++)
            {
                var character = pattern[index];
                switch (character)
                {
                    case '\\' when index + 1 < pattern.Length:
                        expression.Append(Regex.Escape(
                            pattern[++index].ToString()));
                        break;
                    case '*':
                        if (IsLeadingRecursiveGlob(pattern, index))
                        {
                            index += 2;
                            expression.Append("(?:.*/)?");
                        }
                        else if (IsMiddleRecursiveGlob(pattern, index))
                        {
                            index += 2;
                            expression.Append("(?:.*/)?");
                        }
                        else if (IsTrailingRecursiveGlob(pattern, index))
                        {
                            index++;
                            expression.Append(".*");
                        }
                        else
                        {
                            expression.Append("[^/]*");
                        }

                        break;
                    case '?':
                        expression.Append("[^/]");
                        break;
                    case '[':
                        var closing = FindCharacterClassEnd(
                            pattern,
                            index + 1);
                        if (closing < 0)
                        {
                            expression.Append("\\[");
                            break;
                        }

                        expression.Append('[');
                        var classStart = index + 1;
                        if (pattern[classStart] == '!')
                        {
                            expression.Append('^');
                            classStart++;
                        }

                        if (classStart < closing
                            && pattern[classStart] == ']')
                        {
                            expression.Append(']');
                            classStart++;
                        }

                        for (var classIndex = classStart;
                             classIndex < closing;
                             classIndex++)
                        {
                            var classCharacter = pattern[classIndex];
                            expression.Append(classCharacter is '\\' or '^'
                                ? "\\" + classCharacter
                                : classCharacter);
                        }

                        expression.Append(']');
                        index = closing;
                        break;
                    default:
                        expression.Append(Regex.Escape(
                            character.ToString()));
                        break;
                }
            }

            return expression.ToString();
        }

        private static bool IsLeadingRecursiveGlob(
            string pattern,
            int index) =>
            index == 0
            && pattern.Length > 3
            && pattern.StartsWith("**/", StringComparison.Ordinal);

        private static bool IsMiddleRecursiveGlob(
            string pattern,
            int index) =>
            index > 0
            && pattern[index - 1] == '/'
            && index + 3 < pattern.Length
            && pattern[index + 1] == '*'
            && pattern[index + 2] == '/';

        private static bool IsTrailingRecursiveGlob(
            string pattern,
            int index) =>
            index > 0
            && pattern[index - 1] == '/'
            && index + 2 == pattern.Length
            && pattern[index + 1] == '*';

        private static int FindCharacterClassEnd(
            string pattern,
            int start)
        {
            for (var index = start; index < pattern.Length; index++)
            {
                if (pattern[index] == ']' && index > start)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
