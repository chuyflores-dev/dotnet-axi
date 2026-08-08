using System.Text.RegularExpressions;

namespace DotNetAxi.Testing;

internal static partial class CodexBenchmarkCommandEvidence
{
    public static string Classify(
        string command,
        string sandbox,
        IReadOnlyList<string> permittedTools)
    {
        if (SourceSearchCommandRegex().IsMatch(command))
        {
            return "source-search";
        }

        if (RepositoryReadCommandRegex().IsMatch(command))
        {
            return "repository-read";
        }

        if (DotNetCommandRegex().IsMatch(command))
        {
            return "dotnet-sdk";
        }

        if (GitCommandRegex().IsMatch(command))
        {
            return "git";
        }

        return string.Equals(sandbox, "read-only", StringComparison.Ordinal)
               && permittedTools.Contains(
                   "repository-read",
                   StringComparer.Ordinal)
            ? "repository-read"
            : "shell";
    }

    public static bool IsPinnedDnxInvocation(
        string command,
        string packageId,
        string packageVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        var invocation = StripSupportedRedirections(UnwrapShell(command));
        if (ContainsUnquotedControlOperator(invocation))
        {
            return false;
        }

        var tokens = CommandArgumentRegex().Matches(invocation)
            .Select(static match => Unquote(match.Value))
            .ToArray();
        var executable = FindExecutable(tokens);
        if (executable < 0
            || !string.Equals(
                Path.GetFileName(tokens[executable]),
                "dnx",
                StringComparison.OrdinalIgnoreCase)
            || executable + 1 >= tokens.Length
            || !string.Equals(
                tokens[executable + 1],
                $"{packageId}@{packageVersion}",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var delimiter = Array.IndexOf(tokens, "--", executable + 2);
        return delimiter >= 0
               && !tokens
                   .Skip(executable + 2)
                   .Take(delimiter - executable - 2)
                   .Any(static token =>
                       token is "-?" or "-h" or "--help" or "--version"
                       || token.StartsWith(
                           "--version=",
                           StringComparison.Ordinal));
    }

    public static bool ObserveCommandScope(
        string command,
        string? workspacePath,
        ISet<string> files,
        ISet<string> projects)
    {
        if (SourceSearchCommandRegex().IsMatch(command))
        {
            return RejectOutsideSearchPaths(command, workspacePath);
        }

        return ObserveScopeText(command, workspacePath, files, projects);
    }

    public static bool ObserveOutputScope(
        string value,
        string? workspacePath,
        ISet<string> files,
        ISet<string> projects)
    {
        var reportedPaths = ReportedScopePathRegex().Matches(value);
        if (reportedPaths.Count == 0)
        {
            return ObserveScopeText(value, workspacePath, files, projects);
        }

        foreach (Match match in reportedPaths)
        {
            var path = match.Groups["quotedPath"].Success
                ? match.Groups["quotedPath"].Value
                : match.Groups["path"].Value;
            if (!ObservePath(path, workspacePath, files, projects))
            {
                return false;
            }
        }

        return true;
    }

    public static bool ObservePath(
        string value,
        string? workspacePath,
        ISet<string> files,
        ISet<string> projects)
    {
        if (!TryNormalizeScopePath(value, workspacePath, out var normalized))
        {
            return false;
        }

        if (normalized.EndsWith(
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
        {
            projects.Add(normalized);
            return true;
        }

        if (normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            files.Add(normalized);
            return true;
        }

        return false;
    }

    private static bool ObserveScopeText(
        string value,
        string? workspacePath,
        ISet<string> files,
        ISet<string> projects)
    {
        foreach (Match match in ScopePathRegex().Matches(value))
        {
            var path = match.Groups["quotedPath"].Success
                ? match.Groups["quotedPath"].Value
                : match.Groups["path"].Value;
            if (!ObservePath(path, workspacePath, files, projects))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RejectOutsideSearchPaths(
        string command,
        string? workspacePath)
    {
        foreach (Match match in ScopePathRegex().Matches(command))
        {
            var path = match.Groups["quotedPath"].Success
                ? match.Groups["quotedPath"].Value
                : match.Groups["path"].Value;
            if (RequiresMandatoryContainment(path))
            {
                if (!TryNormalizeScopePath(path, workspacePath, out _))
                {
                    return false;
                }

                continue;
            }

            if (IsSearchExpression(path) || !HasExplicitPathShape(path))
            {
                continue;
            }

            if (!TryNormalizeScopePath(path, workspacePath, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RequiresMandatoryContainment(string value) =>
        IsCrossPlatformFullyQualified(value)
        || value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Contains("..", StringComparer.Ordinal);

    private static bool IsSearchExpression(string value) =>
        value.StartsWith('!')
        || value.IndexOfAny(
            ['*', '?', '{', '}', '[', ']', '(', ')', '|', '+', '$', '^'])
            >= 0;

    private static bool HasExplicitPathShape(string value) =>
        IsCrossPlatformFullyQualified(value)
        || value.Contains('/')
        || value.Contains('\\');

    private static bool TryNormalizeScopePath(
        string value,
        string? workspacePath,
        out string normalized)
    {
        var candidate = value;
        if (IsCrossPlatformFullyQualified(candidate))
        {
            if (workspacePath is null
                || !Path.IsPathFullyQualified(candidate))
            {
                normalized = string.Empty;
                return false;
            }

            var workspaceRoot = NormalizeMacOsPrivatePath(
                Path.GetFullPath(workspacePath));
            var candidatePath = NormalizeMacOsPrivatePath(
                Path.GetFullPath(candidate));
            var relative = Path.GetRelativePath(workspaceRoot, candidatePath);
            if (Path.IsPathRooted(relative)
                || relative == ".."
                || relative.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                normalized = string.Empty;
                return false;
            }

            candidate = relative;
        }

        while (candidate.StartsWith("./", StringComparison.Ordinal)
               || candidate.StartsWith(".\\", StringComparison.Ordinal))
        {
            candidate = candidate[2..];
        }

        return PortableRelativePath.TryNormalize(
            candidate,
            normalizeBackslashes: true,
            out normalized);
    }

    private static int FindExecutable(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return -1;
        }

        var index = 0;
        if (string.Equals(
                Path.GetFileName(tokens[index]),
                "env",
                StringComparison.OrdinalIgnoreCase))
        {
            index++;
            while (index < tokens.Count
                   && tokens[index].Contains('=')
                   && !tokens[index].StartsWith("=", StringComparison.Ordinal))
            {
                index++;
            }
        }

        return index < tokens.Count ? index : -1;
    }

    private static string UnwrapShell(string command)
    {
        var trimmed = command.Trim();
        var match = ShellWrapperRegex().Match(trimmed);
        return match.Success ? match.Groups["body"].Value : trimmed;
    }

    private static string StripSupportedRedirections(string command)
    {
        var value = command.Trim();
        while (true)
        {
            var stripped = TrailingRedirectionRegex().Replace(value, string.Empty);
            if (stripped.Length == value.Length)
            {
                return value;
            }

            value = stripped.TrimEnd();
        }
    }

    private static bool ContainsUnquotedControlOperator(string command)
    {
        char quote = '\0';
        foreach (var character in command)
        {
            if (character is '\'' or '"')
            {
                quote = quote == '\0'
                    ? character
                    : quote == character ? '\0' : quote;
                continue;
            }

            if (quote == '\0'
                && (character is ';' or '|' or '&' or '(' or ')' or '<' or '>'
                    || character is '\r' or '\n'))
            {
                return true;
            }
        }

        return quote != '\0';
    }

    private static string Unquote(string value) =>
        value.Length >= 2
        && value[0] is '\'' or '"'
        && value[^1] == value[0]
            ? value[1..^1]
            : value;

    private static bool IsCrossPlatformFullyQualified(string value) =>
        value.StartsWith("/", StringComparison.Ordinal)
        || value.StartsWith("\\\\", StringComparison.Ordinal)
        || DriveRootRegex().IsMatch(value);

    private static string NormalizeMacOsPrivatePath(string path) =>
        OperatingSystem.IsMacOS()
        && path.StartsWith("/private/", StringComparison.Ordinal)
            ? path[8..]
            : path;

    [GeneratedRegex(
        "^(?:(?:/[^/\\s]+/)?(?:zsh|bash|sh)|pwsh|powershell)\\s+(?:-lc|-c|-Command)\\s+(?<quote>[\"'])(?<body>.*)\\k<quote>\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShellWrapperRegex();

    [GeneratedRegex(
        "(?:\"[^\"\\r\\n]*\"|'[^'\\r\\n]*'|[^\\s]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex CommandArgumentRegex();

    [GeneratedRegex(
        "\\s+(?:[0-9]+>&[0-9]+|&>>?\\S+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TrailingRedirectionRegex();

    [GeneratedRegex("^[A-Za-z]:[\\\\/]")]
    private static partial Regex DriveRootRegex();

    [GeneratedRegex(
        "(?:^|[\\s;'\"|&()])(?:rg|grep|find|fd)(?=$|[\\s;'\"|&()])|(?:^|[\\s;'\"|&()])dnaxi\\s+search(?=$|[\\s;'\"|&()])|(?:^|[\\s;'\"|&()])dnx\\s+\\S+[^\\r\\n;|&]*\\s--\\s+search(?=$|[\\s;'\"|&()])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceSearchCommandRegex();

    [GeneratedRegex(
        "(?:^|[\\s;'\"|&()])(?:cat|sed|head|tail|type|Get-Content)(?=$|[\\s;'\"|&()])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryReadCommandRegex();

    [GeneratedRegex(
        "(?:^|[\\s;'\"|&()])dotnet(?=$|[\\s;'\"|&()])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DotNetCommandRegex();

    [GeneratedRegex(
        "(?:^|[\\s;'\"|&()])git(?=$|[\\s;'\"|&()])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GitCommandRegex();

    [GeneratedRegex(
        "(?:(?<quote>[\"'])(?<quotedPath>[^\"'\\r\\n]+\\.(?:csproj|cs))\\k<quote>|(?<path>(?:(?:[A-Za-z]:[\\\\/]|/|\\\\\\\\)?[A-Za-z0-9_.-]+(?:[\\\\/][A-Za-z0-9_.-]+)*)\\.(?:csproj|cs)))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScopePathRegex();

    [GeneratedRegex(
        "^[ \\t]*(?:(?<quote>[\"'])(?<quotedPath>[^\"'\\r\\n]+\\.(?:csproj|cs))\\k<quote>|(?<path>[^\\r\\n]+?\\.(?:csproj|cs)))(?=:\\d+(?::|$)|[ \\t]*$)",
        RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.Multiline)]
    private static partial Regex ReportedScopePathRegex();
}
