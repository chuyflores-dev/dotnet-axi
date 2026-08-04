using System.Text;
using System.Text.RegularExpressions;
using DotNetAxi.Contracts;

namespace DotNetAxi.Workspaces;

public sealed class WorkspacePathTraverser : IWorkspacePathTraverser
{
    private static readonly string[] DefaultGeneratedPathPatterns =
    [
        "**/*.designer.cs",
        "**/*.g.cs",
        "**/*.g.i.cs",
        "**/*.generated.cs",
    ];

    public IReadOnlyList<WorkspaceTraversalPath> Traverse(
        WorkspaceTraversalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workspaceRoot = Path.GetFullPath(request.WorkspaceRoot);
        if (!Directory.Exists(workspaceRoot))
        {
            throw new DirectoryNotFoundException(
                $"Workspace traversal directory '{workspaceRoot}' does not exist.");
        }

        var currentDirectory = Path.GetFullPath(request.CurrentDirectory);
        var pathResolver = new WorkspacePathResolver(
            workspaceRoot,
            currentDirectory);
        var explicitScopes = ResolveExplicitScopes(
            request.ExplicitPaths,
            workspaceRoot,
            currentDirectory,
            pathResolver);
        var configuredExclusions = request.Configuration.ExclusionPatterns
            .Select(static pattern => PathGlob.Parse(pattern))
            .ToArray();
        var generatedPatterns = DefaultGeneratedPathPatterns
            .Select(static pattern => PathGlob.Parse(pattern, ignoreCase: true))
            .Concat(request.Configuration.GeneratedPathPatterns
                .Select(static pattern => PathGlob.Parse(pattern)))
            .ToArray();
        var includeGenerated = request.IncludeGenerated
            ?? request.Configuration.IncludeGeneratedByDefault;
        var paths = new Dictionary<string, WorkspaceTraversalPath>(
            WorkspacePathResolver.PathComparer());
        var initialRules = ReadGitInfoExclude(workspaceRoot, pathResolver);

        var traverseWorkspace = request.ExplicitPaths.Count == 0
            || explicitScopes.Any(scope =>
                !scope.IsExternal
                || PathsIntersect(scope.FullPath, workspaceRoot));
        if (traverseWorkspace)
        {
            EnumerateDirectory(
                new DirectoryInfo(workspaceRoot),
                string.Empty,
                initialRules,
                pathResolver,
                explicitScopes,
                configuredExclusions,
                generatedPatterns,
                includeGenerated,
                readGitIgnore: true,
                excludedDirectory: null,
                paths);
        }

        foreach (var scope in explicitScopes.Where(scope =>
                     scope.IsExternal
                     && !IsSameOrDescendantPath(scope.FullPath, workspaceRoot)))
        {
            if (File.Exists(scope.FullPath))
            {
                AddFile(
                    new FileInfo(scope.FullPath),
                    [],
                    pathResolver,
                    explicitScopes,
                    configuredExclusions,
                    generatedPatterns,
                    includeGenerated,
                    paths);
            }
            else if (Directory.Exists(scope.FullPath))
            {
                EnumerateDirectory(
                    new DirectoryInfo(scope.FullPath),
                    scope.RelativePath,
                    [],
                    pathResolver,
                    explicitScopes,
                    configuredExclusions,
                    generatedPatterns,
                    includeGenerated,
                    readGitIgnore: false,
                    excludedDirectory: traverseWorkspace ? workspaceRoot : null,
                    paths);
            }
        }

        return Array.AsReadOnly(
            paths
                .Values
                .OrderBy(static path => path.RelativePath, StringComparer.Ordinal)
                .ToArray());
    }

    private static void EnumerateDirectory(
        DirectoryInfo directory,
        string relativeDirectory,
        IReadOnlyList<GitIgnoreRule> inheritedRules,
        WorkspacePathResolver pathResolver,
        IReadOnlyList<ExplicitScope> explicitScopes,
        IReadOnlyList<PathGlob> configuredExclusions,
        IReadOnlyList<PathGlob> generatedPatterns,
        bool includeGenerated,
        bool readGitIgnore,
        string? excludedDirectory,
        Dictionary<string, WorkspaceTraversalPath> paths)
    {
        var rules = readGitIgnore
            ? inheritedRules
                .Concat(ReadGitIgnore(directory, relativeDirectory, pathResolver))
                .ToArray()
            : inheritedRules;
        var entries = directory
            .GetFileSystemInfos()
            .OrderBy(static entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in entries)
        {
            if (entry is DirectoryInfo child)
            {
                if ((excludedDirectory is not null
                        && IsSamePath(child.FullName, excludedDirectory))
                    || IsGitMarker(child.Name)
                    || (child.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var childRelativePath = CombineRelativePath(
                    relativeDirectory,
                    child.Name);
                if (IsIgnored(
                        childRelativePath,
                        rules,
                        isDirectory: true))
                {
                    continue;
                }

                EnumerateDirectory(
                    child,
                    childRelativePath,
                    rules,
                    pathResolver,
                    explicitScopes,
                    configuredExclusions,
                    generatedPatterns,
                    includeGenerated,
                    readGitIgnore,
                    excludedDirectory,
                    paths);
                continue;
            }

            if (entry is FileInfo file)
            {
                AddFile(
                    file,
                    rules,
                    pathResolver,
                    explicitScopes,
                    configuredExclusions,
                    generatedPatterns,
                    includeGenerated,
                    paths);
            }
        }
    }

    private static void AddFile(
        FileInfo file,
        IReadOnlyList<GitIgnoreRule> rules,
        WorkspacePathResolver pathResolver,
        IReadOnlyList<ExplicitScope> explicitScopes,
        IReadOnlyList<PathGlob> configuredExclusions,
        IReadOnlyList<PathGlob> generatedPatterns,
        bool includeGenerated,
        Dictionary<string, WorkspaceTraversalPath> paths)
    {
        var policyPath = LexicalPolicyPath(file, pathResolver);
        if (IsGitMarker(file.Name)
            || !TryResolveFile(file, pathResolver, out var path)
            || !IsWithinExplicitScope(file.FullName, path, explicitScopes)
            || IsIgnored(policyPath ?? path.RelativePath, rules, isDirectory: false)
            || configuredExclusions.Any(pattern =>
                pattern.Matches(policyPath ?? path.RelativePath))
            || (!IsExplicitBuildOutput(file.FullName, path, explicitScopes)
                && IsBuildOutput(policyPath ?? path.RelativePath))
            || (!includeGenerated
                && IsGenerated(
                    file,
                    policyPath ?? path.RelativePath,
                    generatedPatterns)))
        {
            return;
        }

        paths.TryAdd(path.RelativePath, path);
    }

    private static IReadOnlyList<ExplicitScope> ResolveExplicitScopes(
        IReadOnlyList<string> explicitPaths,
        string workspaceRoot,
        string currentDirectory,
        WorkspacePathResolver pathResolver)
    {
        var scopes = new Dictionary<string, ExplicitScope>(
            WorkspacePathResolver.PathComparer());
        foreach (var path in explicitPaths)
        {
            var lexicalFullPath = Path.GetFullPath(
                ToNativePath(path),
                currentDirectory);
            if (ContainsDirectoryReparsePoint(
                    lexicalFullPath,
                    workspaceRoot,
                    currentDirectory))
            {
                continue;
            }

            var resolved = pathResolver.ResolveInput(
                path,
                WorkspacePathScope.Explicit);
            scopes.TryAdd(
                resolved.Path,
                new ExplicitScope(
                    lexicalFullPath,
                    resolved.Path,
                    resolved.IsExternal));
        }

        return scopes
            .Values
            .OrderBy(static scope => scope.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ContainsDirectoryReparsePoint(
        string path,
        string workspaceRoot,
        string currentDirectory)
    {
        var root = Path.GetPathRoot(path)!;
        var candidate = root;
        foreach (var segment in Path.GetRelativePath(root, path).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            candidate = Path.Combine(candidate, segment);
            if (IsSameOrDescendantPath(workspaceRoot, candidate)
                || IsSameOrDescendantPath(currentDirectory, candidate))
            {
                continue;
            }

            try
            {
                var attributes = File.GetAttributes(candidate);
                if ((attributes
                        & (FileAttributes.Directory | FileAttributes.ReparsePoint))
                    == (FileAttributes.Directory | FileAttributes.ReparsePoint))
                {
                    return true;
                }
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveFile(
        FileInfo file,
        WorkspacePathResolver pathResolver,
        out WorkspaceTraversalPath path)
    {
        path = default!;
        try
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = file.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not FileInfo targetFile || !targetFile.Exists)
                {
                    return false;
                }
            }

            var resolution = pathResolver.NormalizeOutput(file.FullName);
            path = new WorkspaceTraversalPath(
                resolution.FullPath,
                resolution.Path,
                resolution.IsExternal);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsWithinExplicitScope(
        string lexicalFullPath,
        WorkspaceTraversalPath path,
        IReadOnlyList<ExplicitScope> explicitScopes) =>
        explicitScopes.Count == 0
        ? !path.IsExternal
        : explicitScopes.Any(scope =>
            IsSameOrDescendantPath(lexicalFullPath, scope.FullPath)
            || (!scope.IsExternal
                && IsWithinRelativeScope(path.RelativePath, scope.RelativePath)));

    private static bool IsExplicitBuildOutput(
        string lexicalFullPath,
        WorkspaceTraversalPath path,
        IReadOnlyList<ExplicitScope> explicitScopes) =>
        explicitScopes.Count != 0
        && IsWithinExplicitScope(lexicalFullPath, path, explicitScopes);

    private static bool IsWithinRelativeScope(string path, string scope) =>
        scope.Equals(".", PathComparison())
        || path.Equals(scope, PathComparison())
        || path.StartsWith(scope + "/", PathComparison());

    private static string? LexicalPolicyPath(
        FileInfo file,
        WorkspacePathResolver pathResolver)
    {
        try
        {
            return pathResolver.NormalizeContainedOutput(file.FullName);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool PathsIntersect(string first, string second) =>
        IsSameOrDescendantPath(first, second)
        || IsSameOrDescendantPath(second, first);

    private static bool IsSamePath(string first, string second) =>
        Path.GetFullPath(first).Equals(
            Path.GetFullPath(second),
            PathComparison());

    private static bool IsSameOrDescendantPath(string path, string root)
    {
        var relativePath = Path.GetRelativePath(root, path);
        var normalized = WorkspacePathResolver.NormalizeNativeSeparators(
            relativePath);
        return normalized.Equals(".", StringComparison.Ordinal)
            || (!Path.IsPathRooted(relativePath)
                && !normalized.Equals("..", StringComparison.Ordinal)
                && !normalized.StartsWith("../", StringComparison.Ordinal));
    }

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Split('/').Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));

    private static bool IsGenerated(
        FileInfo file,
        string relativePath,
        IReadOnlyList<PathGlob> generatedPatterns) =>
        generatedPatterns.Any(pattern => pattern.Matches(relativePath))
            || HasGeneratedHeader(file);

    private static bool IsGitMarker(string name) =>
        name.Equals(".git", StringComparison.OrdinalIgnoreCase);

    private static bool HasGeneratedHeader(FileInfo file)
    {
        const int maximumCharacters = 4096;
        try
        {
            using var stream = File.Open(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: maximumCharacters,
                leaveOpen: false);
            var buffer = new char[maximumCharacters];
            var count = reader.ReadBlock(buffer, 0, buffer.Length);
            var header = new string(buffer, 0, count);
            return header.Contains("<auto-generated", StringComparison.OrdinalIgnoreCase)
                || header.Contains("<autogenerated", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsIgnored(
        string relativePath,
        IReadOnlyList<GitIgnoreRule> rules,
        bool isDirectory)
    {
        var ignored = false;
        foreach (var rule in rules)
        {
            if (rule.Matches(relativePath, isDirectory))
            {
                ignored = !rule.IsNegation;
            }
        }

        return ignored;
    }

    private static IReadOnlyList<GitIgnoreRule> ReadGitInfoExclude(
        string workspaceRoot,
        WorkspacePathResolver pathResolver) =>
        ReadIgnoreFile(
            ResolveGitInfoExcludePath(workspaceRoot),
            string.Empty,
            pathResolver,
            allowExternal: true);

    private static IReadOnlyList<GitIgnoreRule> ReadGitIgnore(
        DirectoryInfo directory,
        string relativeDirectory,
        WorkspacePathResolver pathResolver) =>
        ReadIgnoreFile(
            Path.Combine(directory.FullName, ".gitignore"),
            relativeDirectory,
            pathResolver,
            allowExternal: false);

    private static IReadOnlyList<GitIgnoreRule> ReadIgnoreFile(
        string? path,
        string relativeDirectory,
        WorkspacePathResolver pathResolver,
        bool allowExternal)
    {
        if (path is null)
        {
            return [];
        }

        var file = new FileInfo(path);
        if (!file.Exists
            || !TryResolveFile(file, pathResolver, out var resolved)
            || (!allowExternal && resolved.IsExternal))
        {
            return [];
        }

        try
        {
            return File.ReadLines(file.FullName)
                .Select(line => GitIgnoreRule.TryParse(line, relativeDirectory))
                .Where(static rule => rule is not null)
                .Select(static rule => rule!)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? ResolveGitInfoExcludePath(string workspaceRoot)
    {
        var markerPath = Path.Combine(workspaceRoot, ".git");
        if (Directory.Exists(markerPath))
        {
            return Path.Combine(markerPath, "info", "exclude");
        }

        if (!File.Exists(markerPath))
        {
            return null;
        }

        try
        {
            var marker = File.ReadAllText(markerPath).Trim();
            const string prefix = "gitdir:";
            if (!marker.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var gitDirectoryValue = marker[prefix.Length..].Trim();
            if (gitDirectoryValue.Length == 0)
            {
                return null;
            }

            var gitDirectory = Path.GetFullPath(
                gitDirectoryValue,
                Path.GetDirectoryName(markerPath)!);
            if (!Directory.Exists(gitDirectory))
            {
                return null;
            }

            var commonDirectory = gitDirectory;
            var commonDirectoryMarker = Path.Combine(gitDirectory, "commondir");
            if (File.Exists(commonDirectoryMarker))
            {
                var commonDirectoryValue = File.ReadAllText(commonDirectoryMarker)
                    .Trim();
                if (commonDirectoryValue.Length == 0)
                {
                    return null;
                }

                commonDirectory = Path.GetFullPath(
                    commonDirectoryValue,
                    gitDirectory);
                if (!Directory.Exists(commonDirectory))
                {
                    return null;
                }
            }

            return Path.Combine(commonDirectory, "info", "exclude");
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

    private static string CombineRelativePath(string directory, string name) =>
        directory.Length == 0 ? name : directory + "/" + name;

    private static string ToNativePath(string path) =>
        path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed class GitIgnoreRule(
        string relativeDirectory,
        PathGlob pattern,
        bool isNegation,
        bool directoryOnly,
        bool isAnchored,
        bool containsSlash)
    {
        public bool IsNegation { get; } = isNegation;

        public bool Matches(string relativePath, bool isDirectory)
        {
            if (directoryOnly && !isDirectory
                || !TryGetRelativePath(relativePath, out var candidate)
                || (isAnchored && !containsSlash && candidate.Contains('/')))
            {
                return false;
            }

            return pattern.MatchesExact(containsSlash
                ? candidate
                : Path.GetFileName(candidate));
        }

        public static GitIgnoreRule? TryParse(
            string line,
            string relativeDirectory)
        {
            var value = TrimUnescapedTrailingSpaces(line);
            if (value.Length == 0)
            {
                return null;
            }

            var escapedLeadingCharacter = value.Length > 1
                && value[0] == '\\'
                && value[1] is '#' or '!';
            if (value[0] == '#' && !escapedLeadingCharacter)
            {
                return null;
            }

            if (escapedLeadingCharacter)
            {
                value = value[1..];
            }

            var isNegation = !escapedLeadingCharacter && value[0] == '!';
            if (isNegation)
            {
                value = value[1..];
            }

            var directoryOnly = value.EndsWith('/')
                && !IsEscaped(value, value.Length - 1);
            if (directoryOnly)
            {
                value = value[..^1];
            }

            var isAnchored = value.StartsWith('/') && !IsEscaped(value, 0);
            if (isAnchored)
            {
                value = value[1..];
            }

            var containsSlash = value.Contains('/');

            return value.Length == 0
                ? null
                : new GitIgnoreRule(
                    relativeDirectory,
                    PathGlob.Parse(value, normalizeBackslashes: false),
                    isNegation,
                    directoryOnly,
                    isAnchored,
                    containsSlash);
        }

        private bool TryGetRelativePath(string relativePath, out string result)
        {
            if (relativeDirectory.Length == 0)
            {
                result = relativePath;
                return true;
            }

            var prefix = relativeDirectory + "/";
            if (!relativePath.StartsWith(prefix, StringComparison.Ordinal))
            {
                result = string.Empty;
                return false;
            }

            result = relativePath[prefix.Length..];
            return true;
        }

        private static string TrimUnescapedTrailingSpaces(string value)
        {
            var end = value.Length;
            while (end > 0 && value[end - 1] == ' ')
            {
                var slashCount = 0;
                for (var index = end - 2;
                     index >= 0 && value[index] == '\\';
                     index--)
                {
                    slashCount++;
                }

                if (slashCount % 2 != 0)
                {
                    break;
                }

                end--;
            }

            return value[..end];
        }

        private static bool IsEscaped(string value, int index)
        {
            var slashCount = 0;
            for (var previous = index - 1;
                 previous >= 0 && value[previous] == '\\';
                 previous--)
            {
                slashCount++;
            }

            return slashCount % 2 != 0;
        }
    }

    private sealed class PathGlob
    {
        private readonly Regex _expression;
        private readonly bool _matchesAnySegment;

        private PathGlob(Regex expression, bool matchesAnySegment)
        {
            _expression = expression;
            _matchesAnySegment = matchesAnySegment;
        }

        public static PathGlob Parse(
            string pattern,
            bool normalizeBackslashes = true,
            bool ignoreCase = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
            var normalized = normalizeBackslashes
                ? pattern.Replace('\\', '/')
                : pattern;
            var matchesAnySegment = !normalized.Contains('/');
            return new PathGlob(
                new Regex(
                    "\\A" + ToRegularExpression(normalized) + "\\z",
                    RegexOptions.CultureInvariant
                    | RegexOptions.ExplicitCapture
                    | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None)),
                matchesAnySegment);
        }

        public bool Matches(string path)
        {
            if (_matchesAnySegment)
            {
                return path.Split('/').Any(segment => _expression.IsMatch(segment));
            }

            return _expression.IsMatch(path);
        }

        public bool MatchesExact(string path) => _expression.IsMatch(path);

        private static string ToRegularExpression(string pattern)
        {
            var expression = new StringBuilder();
            for (var index = 0; index < pattern.Length; index++)
            {
                var character = pattern[index];
                switch (character)
                {
                    case '\\' when index + 1 < pattern.Length:
                        expression.Append(Regex.Escape(pattern[++index].ToString()));
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
                        var closing = FindCharacterClassEnd(pattern, index + 1);
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

                        if (classStart < closing && pattern[classStart] == ']')
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
                        expression.Append(Regex.Escape(character.ToString()));
                        break;
                }
            }

            return expression.ToString();
        }

        private static bool IsLeadingRecursiveGlob(string pattern, int index) =>
            index == 0
            && pattern.Length > 3
            && pattern.StartsWith("**/", StringComparison.Ordinal);

        private static bool IsMiddleRecursiveGlob(string pattern, int index) =>
            index > 0
            && pattern[index - 1] == '/'
            && index + 3 < pattern.Length
            && pattern[index + 1] == '*'
            && pattern[index + 2] == '/';

        private static bool IsTrailingRecursiveGlob(string pattern, int index) =>
            index > 0
            && pattern[index - 1] == '/'
            && index + 2 == pattern.Length
            && pattern[index + 1] == '*';

        private static int FindCharacterClassEnd(string pattern, int start)
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

    private sealed record ExplicitScope(
        string FullPath,
        string RelativePath,
        bool IsExternal);
}
