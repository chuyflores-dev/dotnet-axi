using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DotNetAxi.Testing;

internal static partial class CodexBenchmarkCommandEvidence
{
    internal const string ExpectedStaleSymbolId =
        "symbol/v2/UmVjb25jaWxl/d7d5e6525f53c615e5e7d46a36dd2d0269248cf56534cbba2547b2de5f1c28bd/2206123742bcff880296a425da1e816f4a04d13a63713a1cbbc1de38be31274c";
    internal const string ExpectedAmbiguousSymbolId =
        "symbol/v2/UmVsb2NhdGVkV2lkZ2V0/fb7c47ef95fe7df477afd00aa0fbb4b91ee03cdc6d4103f6310194cbb3ac60b9/0a0d241c410272d0c47d80f4991f2b8f28a6f6f35c414659e21ba3be210a23d6";

    public static string Classify(
        string command,
        string sandbox,
        IReadOnlyList<string> permittedTools)
    {
        var invocation = UnwrapShell(command);
        if (SourceSearchCommandRegex().IsMatch(invocation))
        {
            return "source-search";
        }

        if (RepositoryReadCommandRegex().IsMatch(invocation))
        {
            return "repository-read";
        }

        if (IsExecutableNamed(invocation, "dotnet"))
        {
            return "dotnet-sdk";
        }

        if (IsExecutableNamed(invocation, "git"))
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
        string packageVersion,
        string? packageSource = null,
        string? packageSourceEnvironmentVariable = null,
        string? expectedCapability = null,
        string? expectedDnxExecutablePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        var escapeCharacter = GetControlEscapeCharacter(command);
        var usesPosixDirectAssignmentGrammar =
            UsesPosixDirectAssignmentGrammar(command);
        if (usesPosixDirectAssignmentGrammar
            && ContainsUnquotedNonPosixWhitespace(
                command,
                escapeCharacter))
        {
            return false;
        }

        var invocation = StripSupportedRedirections(UnwrapShell(command));
        if (usesPosixDirectAssignmentGrammar
            && ContainsUnquotedNonPosixWhitespace(
                invocation,
                escapeCharacter))
        {
            return false;
        }

        if (ContainsUnquotedControlOperator(
                invocation,
                escapeCharacter))
        {
            return false;
        }

        var assignmentNames = Array.Empty<string>();
        if (usesPosixDirectAssignmentGrammar)
        {
            invocation = StripLeadingPosixEnvironmentAssignments(
                invocation,
                out assignmentNames);
        }

        if (assignmentNames.Contains(
                "PATH",
                StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawTokens = CommandArgumentRegex().Matches(invocation)
            .Select(static match => match.Value)
            .ToArray();
        var tokens = rawTokens
            .Select(Unquote)
            .ToArray();
        var executable = FindExecutable(tokens);
        if (executable < 0
            || !IsDnxExecutableName(tokens[executable])
            || executable + 1 >= tokens.Length
            || !string.Equals(
                tokens[executable + 1],
                $"{packageId}@{packageVersion}",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expectedDnxExecutablePath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                expectedDnxExecutablePath);
            if (tokens.Take(executable).Any(static token =>
                    token.StartsWith(
                        "PATH=",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var expectedPath = Path.GetFullPath(expectedDnxExecutablePath);
            var expectedName = Path.GetFileName(expectedPath);
            var executableToken = tokens[executable];
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var bareExecutable = !executableToken.Contains('/')
                                 && !executableToken.Contains('\\');
            var bareNameMatches = bareExecutable
                                  && (string.Equals(
                                      executableToken,
                                      expectedName,
                                      comparison)
                                      || (OperatingSystem.IsWindows()
                                          && string.Equals(
                                              expectedName,
                                              "dnx.exe",
                                              StringComparison.OrdinalIgnoreCase)
                                          && string.Equals(
                                              executableToken,
                                              "dnx",
                                              StringComparison.OrdinalIgnoreCase)));
            if (!bareNameMatches
                && !string.Equals(
                    executableToken,
                    expectedPath,
                    comparison))
            {
                return false;
            }
        }

        var delimiter = Array.IndexOf(tokens, "--", executable + 2);
        if (delimiter < 0)
        {
            return false;
        }

        var options = tokens
            .Skip(executable + 2)
            .Take(delimiter - executable - 2)
            .ToArray();
        if (options.Any(static token =>
                token is "-?" or "-h" or "--help" or "--version"
                || token.StartsWith(
                    "--version=",
                    StringComparison.Ordinal)
                || token is "--add-source"
                || token.StartsWith(
                    "--add-source=",
                    StringComparison.Ordinal)
                || token.StartsWith(
                    "--source=",
                    StringComparison.Ordinal)))
        {
            return false;
        }

        if (packageSource is null)
        {
            return true;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            packageSourceEnvironmentVariable);
        var sourceIndexes = options
            .Select((token, index) => (token, index))
            .Where(static item => item.token == "--source")
            .Select(static item => item.index)
            .ToArray();
        var verbosityIndexes = options
            .Select((token, index) => (token, index))
            .Where(static item => item.token == "--verbosity")
            .Select(static item => item.index)
            .ToArray();
        if (sourceIndexes.Length != 1
            || verbosityIndexes.Length != 1
            || sourceIndexes[0] + 1 >= options.Length
            || verbosityIndexes[0] + 1 >= options.Length
            || !string.Equals(
                options[verbosityIndexes[0] + 1],
                "quiet",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var source = options[sourceIndexes[0] + 1];
        var rawSource = rawTokens[
            executable + 2 + sourceIndexes[0] + 1];
        var singleQuotedEnvironmentReference = rawSource.Length >= 2
                                               && rawSource[0] == '\''
                                               && rawSource[^1] == '\''
                                               && !string.Equals(
                                                   source,
                                                   packageSource,
                                                   StringComparison.Ordinal);
        var sourceMatches = string.Equals(
                                source,
                                packageSource,
                                StringComparison.Ordinal)
                            || string.Equals(
                                source,
                                $"${packageSourceEnvironmentVariable}",
                                StringComparison.Ordinal)
                            || string.Equals(
                                source,
                                $"${{{packageSourceEnvironmentVariable}}}",
                                StringComparison.Ordinal);
        return !singleQuotedEnvironmentReference
               && sourceMatches
               && (expectedCapability is null
                   || MatchesCapabilityRoute(
                       tokens.Skip(delimiter + 1).ToArray(),
                       expectedCapability));
    }

    private static bool MatchesCapabilityRoute(
        IReadOnlyList<string> arguments,
        string capability)
    {
        if (arguments.Any(static argument =>
                argument is "-?" or "-h" or "--help"))
        {
            return false;
        }

        string[] route;
        var requiresRegex = false;
        switch (capability)
        {
            case "search.file":
                route = ["search", "file"];
                break;
            case "search.text.literal":
                route = ["search", "text"];
                break;
            case "search.text.regex":
                route = ["search", "text"];
                requiresRegex = true;
                break;
            case "search.syntax.attributed-class":
                route = ["search", "syntax", "class"];
                break;
            case "search.syntax.catch":
                route = ["search", "syntax", "catch"];
                break;
            case "search.syntax.invocation":
                route = ["search", "syntax", "invocation"];
                break;
            case "search.syntax.object-creation":
                route = ["search", "syntax", "object-creation"];
                break;
            case "search.symbol.declaration":
                route = ["search", "symbol"];
                break;
            case "show.symbol.identity":
                route = ["show", "symbol"];
                break;
            case "search.syntax.verify":
                route = ["search", "syntax", "invocation"];
                break;
            case "show.document":
                route = ["show", "document"];
                break;
            case "outline.syntax":
                route = ["outline"];
                break;
            case "context.symbol":
                route = ["context", "symbol"];
                break;
            default:
                return false;
        }

        if (!arguments.Take(route.Length).SequenceEqual(
                route,
                StringComparer.Ordinal))
        {
            return false;
        }

        if (capability == "search.syntax.attributed-class")
        {
            return HasSingleNonBlankOption(
                arguments.Skip(route.Length).ToArray(),
                "--attribute");
        }

        if (capability == "search.syntax.verify")
        {
            return TryGetBooleanOption(
                       arguments.Skip(route.Length).ToArray(),
                       "--verify",
                       out var verifyPresent,
                       out var verifyEnabled)
                   && verifyPresent
                   && verifyEnabled;
        }

        if (capability is "search.symbol.declaration"
            or "show.symbol.identity"
            or "show.document"
            or "outline.syntax"
            or "context.symbol")
        {
            return arguments.Count > route.Length
                   && !arguments[route.Length].StartsWith(
                       "-",
                       StringComparison.Ordinal);
        }

        if (capability is not ("search.text.literal" or "search.text.regex"))
        {
            return true;
        }

        if (!TryGetBooleanOption(
                arguments.Skip(route.Length).ToArray(),
                "--regex",
                out var regexPresent,
                out var regexEnabled))
        {
            return false;
        }

        return requiresRegex
            ? regexPresent && regexEnabled
            : !regexPresent || !regexEnabled;
    }

    public static bool TryParsePinnedDnxInvocation(
        string command,
        string packageId,
        string packageVersion,
        string packageSource,
        string packageSourceEnvironmentVariable,
        string expectedDnxExecutablePath,
        out CodexBenchmarkDnxInvocation? invocation)
    {
        invocation = null;
        if (!IsPinnedDnxInvocation(
                command,
                packageId,
                packageVersion,
                packageSource,
                packageSourceEnvironmentVariable,
                expectedDnxExecutablePath: expectedDnxExecutablePath)
            || !TryGetDnxArguments(command, out var arguments)
            || !TryGetRoute(arguments, out var route, out var routeLength))
        {
            return false;
        }

        var routeArguments = arguments.Skip(routeLength).ToArray();
        if (!TryGetRouteTarget(route, routeArguments, out var target)
            || !TryGetSelector(
                routeArguments,
                out var selectorKind,
                out var selectorValue)
            || !TryGetBooleanOption(
                routeArguments,
                "--include-tests",
                out _,
                out var includeTests)
            || !TryGetBooleanOption(
                routeArguments,
                "--include-generated",
                out _,
                out var includeGenerated))
        {
            return false;
        }

        invocation = new CodexBenchmarkDnxInvocation(
            route,
            target,
            selectorKind,
            selectorValue,
            includeTests,
            includeGenerated,
            Array.AsReadOnly(arguments.ToArray()));
        return true;
    }

    private static bool TryGetRouteTarget(
        string route,
        IReadOnlyList<string> arguments,
        out string? target)
    {
        target = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                if (target is not null)
                {
                    return false;
                }

                target = argument;
                continue;
            }

            var equals = argument.IndexOf('=');
            if (equals > 2)
            {
                if (RouteOptionArity(argument[..equals]) < 0)
                {
                    return false;
                }

                continue;
            }

            var arity = RouteOptionArity(argument);
            if (arity < 0)
            {
                return false;
            }

            if (arity == 0)
            {
                if (index + 1 < arguments.Count
                    && bool.TryParse(arguments[index + 1], out _))
                {
                    index++;
                }

                continue;
            }

            if (index + 1 >= arguments.Count)
            {
                return false;
            }

            if (arity == 1)
            {
                index++;
                continue;
            }

            while (index + 1 < arguments.Count
                   && !arguments[index + 1].StartsWith(
                       "--",
                       StringComparison.Ordinal))
            {
                index++;
            }
        }

        var requiresTarget = route is "search symbol"
            or "show symbol"
            or "show document"
            or "outline"
            or "context symbol";
        return requiresTarget ? target is not null : target is null;
    }

    private static int RouteOptionArity(string option) => option switch
    {
        "--full" or "--include-generated" or "--include-tests"
            or "--verify" or "--regex" => 0,
        "--limit" or "--namespace" or "--project" or "--solution"
            or "--max-chars" or "--start-line" or "--end-line"
            or "--name" or "--attribute" or "--type" => 1,
        "--accessibility" or "--fields" or "--kind" or "--path"
            or "--include" => int.MaxValue,
        _ => -1,
    };

    public static CodexBenchmarkTaskRouteEvidence MatchTaskRouteVector(
        string taskId,
        IReadOnlyList<AgentBenchmarkToolCall> calls,
        string packageId,
        string packageVersion,
        string packageSource,
        string packageSourceEnvironmentVariable,
        string expectedDnxExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(calls);

        var expected = ExpectedTaskRouteVector(taskId);
        if (expected.Count == 0)
        {
            return CodexBenchmarkTaskRouteEvidence.None;
        }

        var parsed = calls
            .OrderBy(static call => call.Sequence)
            .Select(call =>
            {
                var matched = TryParsePinnedDnxInvocation(
                    call.Name,
                    packageId,
                    packageVersion,
                    packageSource,
                    packageSourceEnvironmentVariable,
                    expectedDnxExecutablePath,
                    out var command);
                return matched
                    ? new CodexBenchmarkTaskRouteStep(
                        call.Sequence,
                        call.Succeeded,
                        command!)
                    : null;
            })
            .Where(static step => step is not null)
            .Select(static step => step!)
            .ToArray();

        var exact = FindOrderedVector(parsed, expected, requireSuccess: false);
        if (exact is null)
        {
            return CodexBenchmarkTaskRouteEvidence.None;
        }

        var successful = FindOrderedVector(parsed, expected, requireSuccess: true);
        return new CodexBenchmarkTaskRouteEvidence(
            true,
            successful is not null,
            successful ?? exact);
    }

    private static IReadOnlyList<CodexBenchmarkTaskRouteStep>?
        FindOrderedVector(
            IReadOnlyList<CodexBenchmarkTaskRouteStep> parsed,
            IReadOnlyList<Func<CodexBenchmarkDnxInvocation, bool>> expected,
            bool requireSuccess)
    {
        for (var start = 0; start < parsed.Count; start++)
        {
            var matched = new List<CodexBenchmarkTaskRouteStep>(
                expected.Count);
            var expectedIndex = 0;
            for (var index = start;
                 index < parsed.Count && expectedIndex < expected.Count;
                 index++)
            {
                var step = parsed[index];
                if ((!requireSuccess || step.Succeeded)
                    && expected[expectedIndex](step.Invocation))
                {
                    matched.Add(step);
                    expectedIndex++;
                }
            }

            if (expectedIndex == expected.Count)
            {
                return Array.AsReadOnly(matched.ToArray());
            }
        }

        return null;
    }

    private static IReadOnlyList<Func<CodexBenchmarkDnxInvocation, bool>>
        ExpectedTaskRouteVector(string taskId) => taskId switch
        {
            "test-symbol-explicit-scope" =>
            [
                command => IsRoute(command, "search symbol", "ScopeProbe")
                           && HasSelector(
                               command,
                               "solution",
                               "Workspace.slnx")
                           && command.IncludeTests
                           && !command.IncludeGenerated,
            ],
            "symbol-owner-framework-variants" =>
            [
                command => IsRoute(command, "search symbol", "LedgerService")
                           && IsCoreProjectScope(command),
            ],
            "fresh-symbol-identity-show" =>
            [
                command => IsRoute(command, "search symbol", "Format")
                           && IsCoreProjectScope(command),
                command => IsSymbolIdentityRoute(command, "show symbol")
                           && IsCoreProjectScope(command),
            ],
            "stale-symbol-correction" =>
            [
                command => IsSymbolIdentityRoute(command, "show symbol")
                           && IsCoreProjectScope(command)
                           && string.Equals(
                               command.Target,
                               ExpectedStaleSymbolId,
                               StringComparison.Ordinal),
            ],
            "ambiguous-symbol-correction" =>
            [
                command => IsSymbolIdentityRoute(command, "show symbol")
                           && IsCoreProjectScope(command)
                           && string.Equals(
                               command.Target,
                               ExpectedAmbiguousSymbolId,
                               StringComparison.Ordinal),
            ],
            "syntax-candidate-partial-verification" =>
            [
                command => IsRoute(
                               command,
                               "search syntax invocation",
                               targetFragment: null)
                           && HasSelector(
                               command,
                               "path",
                               "loose/UnownedCandidate.cs")
                           && HasOptionValue(
                               command.Arguments,
                               "--name",
                               "MissingAudit")
                           && HasEnabledOption(
                               command.Arguments,
                               "--verify")
                           && !command.IncludeTests
                           && !command.IncludeGenerated,
            ],
            "bounded-symbol-show" =>
            [
                command => IsRoute(command, "search symbol", "LedgerService")
                           && IsCoreProjectScope(command),
                command => IsSymbolIdentityRoute(command, "show symbol")
                           && IsCoreProjectScope(command)
                           && HasOptionValue(
                               command.Arguments,
                               "--max-chars",
                               "24"),
            ],
            "document-exact-line-span" =>
            [
                command => IsRoute(
                               command,
                               "show document",
                               targetFragment: null)
                           && string.Equals(
                               NormalizeRelativePath(command.Target),
                               "docs/Runbook.txt",
                               StringComparison.Ordinal)
                           && command.SelectorKind == "default"
                           && HasOptionValue(
                               command.Arguments,
                               "--start-line",
                               "5")
                           && HasOptionValue(
                               command.Arguments,
                               "--end-line",
                               "6")
                           && !command.IncludeTests
                           && !command.IncludeGenerated,
            ],
            "symbol-outline" =>
            [
                command => IsRoute(command, "search symbol", "LedgerService")
                           && IsCoreProjectScope(command),
                command => IsSymbolIdentityRoute(command, "outline")
                           && IsCoreProjectScope(command),
            ],
            "context-whole-section-truncation" =>
            [
                command => IsRoute(command, "search symbol", "LedgerService")
                           && IsCoreProjectScope(command),
                command => IsSymbolIdentityRoute(command, "context symbol")
                           && IsCoreProjectScope(command)
                           && HasExactOptionValues(
                               command.Arguments,
                               "--include",
                               ["declaration", "owner", "document", "outline"])
                           && HasOptionValue(
                               command.Arguments,
                               "--max-chars",
                               "0"),
            ],
            _ => [],
        };

    private static bool IsCoreProjectScope(
        CodexBenchmarkDnxInvocation command) =>
        HasSelector(command, "project", "src/Core/Core.csproj")
        && !command.IncludeTests
        && !command.IncludeGenerated;

    private static bool IsSymbolIdentityRoute(
        CodexBenchmarkDnxInvocation command,
        string route) =>
        string.Equals(command.Route, route, StringComparison.Ordinal)
        && command.Target is not null
        && command.Target.StartsWith("symbol/v2/", StringComparison.Ordinal);

    private static bool IsRoute(
        CodexBenchmarkDnxInvocation command,
        string route,
        string? targetFragment) =>
        string.Equals(command.Route, route, StringComparison.Ordinal)
        && (targetFragment is null
            || MatchesSearchTarget(command.Target, targetFragment));

    private static bool MatchesSearchTarget(
        string? actual,
        string expectedName)
    {
        var accepted = expectedName switch
        {
            "ScopeProbe" =>
                new[] { "ScopeProbe", "SymbolContext.Tests.ScopeProbe" },
            "LedgerService" =>
                new[]
                {
                    "LedgerService",
                    "SymbolContext.Product.LedgerService",
                },
            "Format" =>
                new[]
                {
                    "Format",
                    "LedgerService.Format",
                    "SymbolContext.Product.LedgerService.Format",
                },
            _ => [expectedName],
        };
        return accepted.Contains(actual, StringComparer.Ordinal);
    }

    private static bool HasSelector(
        CodexBenchmarkDnxInvocation command,
        string kind,
        string expectedValue) =>
        string.Equals(command.SelectorKind, kind, StringComparison.Ordinal)
        && string.Equals(
            NormalizeRelativePath(command.SelectorValue),
            NormalizeRelativePath(expectedValue),
            StringComparison.Ordinal);

    private static bool HasEnabledOption(
        IReadOnlyList<string> arguments,
        string option) =>
        TryGetBooleanOption(
            arguments,
            option,
            out var present,
            out var enabled)
        && present
        && enabled;

    private static bool HasOptionValue(
        IReadOnlyList<string> arguments,
        string option,
        string expectedValue) =>
        TryGetSingleOptionValue(
            arguments,
            option,
            out var present,
            out var value)
        && present
        && string.Equals(value, expectedValue, StringComparison.Ordinal);

    private static bool HasExactOptionValues(
        IReadOnlyList<string> arguments,
        string option,
        IReadOnlyList<string> expectedValues)
    {
        var values = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], option, StringComparison.Ordinal))
            {
                continue;
            }

            for (index++;
                 index < arguments.Count
                 && !arguments[index].StartsWith("--", StringComparison.Ordinal);
                 index++)
            {
                values.AddRange(arguments[index].Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
            }

            index--;
        }

        return values.Distinct(StringComparer.Ordinal).Order().SequenceEqual(
            expectedValues.Distinct(StringComparer.Ordinal).Order(),
            StringComparer.Ordinal);
    }

    private static bool TryGetDnxArguments(
        string command,
        out string[] arguments)
    {
        arguments = [];
        var invocation = StripSupportedRedirections(UnwrapShell(command));
        if (UsesPosixDirectAssignmentGrammar(command))
        {
            invocation = StripLeadingPosixEnvironmentAssignments(
                invocation,
                out _);
        }

        var tokens = CommandArgumentRegex().Matches(invocation)
            .Select(static match => Unquote(match.Value))
            .ToArray();
        var executable = FindExecutable(tokens);
        if (executable < 0)
        {
            return false;
        }

        var delimiter = Array.IndexOf(tokens, "--", executable + 2);
        if (delimiter < 0 || delimiter + 1 >= tokens.Length)
        {
            return false;
        }

        arguments = tokens[(delimiter + 1)..];
        return true;
    }

    private static bool TryGetRoute(
        IReadOnlyList<string> arguments,
        out string route,
        out int routeLength)
    {
        route = string.Empty;
        routeLength = 0;
        string[] routeParts;
        if (arguments.Count >= 3
            && arguments[0] == "search"
            && arguments[1] == "syntax")
        {
            routeParts = [arguments[0], arguments[1], arguments[2]];
        }
        else if (arguments.Count >= 2
                 && arguments[0] is "search" or "show" or "context")
        {
            routeParts = [arguments[0], arguments[1]];
        }
        else if (arguments.Count >= 1 && arguments[0] == "outline")
        {
            routeParts = [arguments[0]];
        }
        else
        {
            return false;
        }

        route = string.Join(' ', routeParts);
        routeLength = routeParts.Length;
        return true;
    }

    private static bool TryGetSelector(
        IReadOnlyList<string> arguments,
        out string kind,
        out string? value)
    {
        kind = "default";
        value = null;
        foreach (var candidate in new[] { "solution", "project", "path" })
        {
            if (!TryGetSingleOptionValue(
                    arguments,
                    $"--{candidate}",
                    out var present,
                    out var candidateValue))
            {
                return false;
            }

            if (!present)
            {
                continue;
            }

            if (kind != "default")
            {
                return false;
            }

            kind = candidate;
            value = candidateValue;
        }

        return true;
    }

    private static bool TryGetSingleOptionValue(
        IReadOnlyList<string> arguments,
        string option,
        out bool present,
        out string? value)
    {
        present = false;
        value = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            string candidate;
            if (string.Equals(argument, option, StringComparison.Ordinal))
            {
                if (++index >= arguments.Count)
                {
                    return false;
                }

                candidate = arguments[index];
            }
            else if (argument.StartsWith(
                         $"{option}=",
                         StringComparison.Ordinal))
            {
                candidate = argument[(option.Length + 1)..];
            }
            else
            {
                continue;
            }

            if (present
                || string.IsNullOrWhiteSpace(candidate)
                || candidate.StartsWith("-", StringComparison.Ordinal))
            {
                return false;
            }

            present = true;
            value = candidate;
        }

        return true;
    }

    private static string? NormalizeRelativePath(string? value)
    {
        var normalized = value?.Replace('\\', '/');
        while (normalized?.StartsWith("./", StringComparison.Ordinal) == true)
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static bool HasSingleNonBlankOption(
        IReadOnlyList<string> arguments,
        string option)
    {
        var count = 0;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            string value;
            if (string.Equals(argument, option, StringComparison.Ordinal))
            {
                if (++index >= arguments.Count)
                {
                    return false;
                }

                value = arguments[index];
            }
            else if (argument.StartsWith(
                         $"{option}=",
                         StringComparison.Ordinal))
            {
                value = argument[(option.Length + 1)..];
            }
            else
            {
                continue;
            }

            count++;
            if (string.IsNullOrWhiteSpace(value)
                || value[0] == '-')
            {
                return false;
            }
        }

        return count == 1;
    }

    private static bool TryGetBooleanOption(
        IReadOnlyList<string> arguments,
        string option,
        out bool present,
        out bool enabled)
    {
        present = false;
        enabled = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            bool value;
            if (string.Equals(argument, option, StringComparison.Ordinal))
            {
                value = true;
                if (index + 1 < arguments.Count
                    && bool.TryParse(arguments[index + 1], out var nextValue))
                {
                    value = nextValue;
                    index++;
                }
            }
            else if (argument.StartsWith(
                         $"{option}=",
                         StringComparison.Ordinal))
            {
                if (!bool.TryParse(
                        argument[(option.Length + 1)..],
                        out value))
                {
                    return false;
                }
            }
            else
            {
                continue;
            }

            if (present)
            {
                return false;
            }

            present = true;
            enabled = value;
        }

        return true;
    }

    public static bool ObserveCommandScope(
        string command,
        string? workspacePath,
        ISet<string> files,
        ISet<string> projects)
    {
        var invocation = UnwrapShell(command);
        if (SourceSearchCommandRegex().IsMatch(invocation))
        {
            return RejectOutsideSearchPaths(invocation, workspacePath);
        }

        return ObserveScopeText(invocation, workspacePath, files, projects);
    }

    public static bool ObserveOutputScope(
        string value,
        string? workspacePath,
        ISet<string> files,
        ISet<string> projects)
    {
        var remaining = new List<string>();
        IReadOnlyList<int> compactPathColumns = [];
        var compactColumnCount = 0;
        var compactRowsRemaining = 0;
        foreach (var line in value.Split('\n'))
        {
            if (TryReadCompactOutputPathTableHeader(
                    line,
                    out var pathColumns,
                    out var columnCount,
                    out var rowCount))
            {
                compactPathColumns = pathColumns;
                compactColumnCount = columnCount;
                compactRowsRemaining = rowCount;
                continue;
            }

            if (compactRowsRemaining > 0)
            {
                if (TryObserveCompactOutputPathRow(
                        line,
                        compactColumnCount,
                        compactPathColumns,
                        workspacePath,
                        files,
                        projects,
                        out var valid))
                {
                    if (!valid)
                    {
                        return false;
                    }

                    compactRowsRemaining--;
                    continue;
                }

                compactPathColumns = [];
                compactColumnCount = 0;
                compactRowsRemaining = 0;
            }

            if (!TryObserveStructuredOutputPathLine(
                    line,
                    workspacePath,
                    files,
                    projects))
            {
                remaining.Add(line);
            }
        }

        var unstructured = string.Join('\n', remaining);
        var reportedPaths = ReportedScopePathRegex().Matches(unstructured);
        if (reportedPaths.Count == 0)
        {
            return ObserveScopeText(
                unstructured,
                workspacePath,
                files,
                projects);
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

    private static bool TryObserveStructuredOutputPathLine(
        string line,
        string? workspacePath,
        ISet<string> files,
        ISet<string> projects)
    {
        var value = line.Trim();
        if (value.StartsWith("- ", StringComparison.Ordinal))
        {
            value = value[2..].TrimStart();
        }

        var separator = value.IndexOf(':');
        if (separator <= 0)
        {
            return false;
        }

        var key = value[..separator];
        if (key is not "path" and not "file" and not "project"
            && !key.StartsWith("paths[", StringComparison.Ordinal)
            && !key.StartsWith("projects[", StringComparison.Ordinal)
            && !key.StartsWith(
                "owning_projects[",
                StringComparison.Ordinal))
        {
            return false;
        }

        var paths = value[(separator + 1)..].Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        foreach (var path in paths)
        {
            var normalized = path.Trim('"');
            if (!normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !normalized.EndsWith(
                    ".csproj",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ObservePath(normalized, workspacePath, files, projects))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadCompactOutputPathTableHeader(
        string line,
        out IReadOnlyList<int> pathColumns,
        out int columnCount,
        out int rowCount)
    {
        pathColumns = [];
        columnCount = 0;
        rowCount = 0;

        var value = line.Trim();
        if (value.StartsWith("- ", StringComparison.Ordinal))
        {
            value = value[2..].TrimStart();
        }

        var openBracket = value.IndexOf('[');
        var closeBracket = value.IndexOf(']', openBracket + 1);
        var openBrace = value.IndexOf('{', closeBracket + 1);
        var closeBrace = value.IndexOf('}', openBrace + 1);
        var key = openBracket > 0 ? value[..openBracket] : string.Empty;
        if (key.Length == 0
            || key[0] is not ('_' or >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            || key.Any(character =>
                character is not ('_' or >= 'A' and <= 'Z'
                    or >= 'a' and <= 'z' or >= '0' and <= '9'))
            || closeBracket <= openBracket + 1
            || openBrace != closeBracket + 1
            || closeBrace <= openBrace + 1
            || !string.Equals(
                value[(closeBrace + 1)..].Trim(),
                ":",
                StringComparison.Ordinal)
            || !int.TryParse(
                value[(openBracket + 1)..closeBracket],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out rowCount))
        {
            return false;
        }

        var columns = value[(openBrace + 1)..closeBrace].Split(
            ',',
            StringSplitOptions.TrimEntries);
        if (columns.Length == 0 || columns.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        var selected = new List<int>();
        for (var index = 0; index < columns.Length; index++)
        {
            if (columns[index] is "path" or "file" or "project"
                || columns[index].EndsWith("_path", StringComparison.Ordinal)
                || columns[index].EndsWith("_file", StringComparison.Ordinal)
                || columns[index].EndsWith("_project", StringComparison.Ordinal))
            {
                selected.Add(index);
            }
        }

        if (selected.Count == 0 || rowCount <= 0)
        {
            return false;
        }

        pathColumns = selected;
        columnCount = columns.Length;
        return true;
    }

    private static bool TryObserveCompactOutputPathRow(
        string line,
        int expectedColumnCount,
        IReadOnlyList<int> pathColumns,
        string? workspacePath,
        ISet<string> files,
        ISet<string> projects,
        out bool valid)
    {
        valid = true;
        if (!TryParseCompactOutputRow(
                line,
                expectedColumnCount,
                out var fields))
        {
            return false;
        }

        foreach (var pathColumn in pathColumns)
        {
            if (string.Equals(
                    fields[pathColumn],
                    "null",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!ObservePath(
                    fields[pathColumn],
                    workspacePath,
                    files,
                    projects))
            {
                valid = false;
                return true;
            }
        }

        return true;
    }

    private static bool TryParseCompactOutputRow(
        string line,
        int expectedColumnCount,
        out IReadOnlyList<string> fields)
    {
        var parsed = new List<string>(expectedColumnCount);
        var current = new StringBuilder();
        var value = line.Trim();
        var quoted = false;
        var closedQuote = false;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (quoted)
            {
                if (character == '"')
                {
                    quoted = false;
                    closedQuote = true;
                    continue;
                }

                if (character != '\\')
                {
                    current.Append(character);
                    continue;
                }

                if (++index >= value.Length)
                {
                    fields = [];
                    return false;
                }

                var escaped = value[index];
                if (escaped == 'u')
                {
                    if (index + 4 >= value.Length
                        || !ushort.TryParse(
                            value.AsSpan(index + 1, 4),
                            NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture,
                            out var codeUnit))
                    {
                        fields = [];
                        return false;
                    }

                    current.Append((char)codeUnit);
                    index += 4;
                    continue;
                }

                current.Append(escaped switch
                {
                    '\\' => '\\',
                    '"' => '"',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => '\0',
                });
                if (current[^1] == '\0')
                {
                    fields = [];
                    return false;
                }

                continue;
            }

            if (character == ',' && !closedQuote)
            {
                parsed.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            if (character == ',' && closedQuote)
            {
                parsed.Add(current.ToString());
                current.Clear();
                closedQuote = false;
                continue;
            }

            if (character == '"'
                && current.Length == 0
                && !closedQuote)
            {
                quoted = true;
                continue;
            }

            if (closedQuote && !char.IsWhiteSpace(character))
            {
                fields = [];
                return false;
            }

            if (!closedQuote)
            {
                current.Append(character);
            }
        }

        if (quoted)
        {
            fields = [];
            return false;
        }

        parsed.Add(closedQuote
            ? current.ToString()
            : current.ToString().Trim());
        if (parsed.Count != expectedColumnCount)
        {
            fields = [];
            return false;
        }

        fields = parsed;
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
                   && EnvironmentAssignmentRegex().IsMatch(tokens[index]))
            {
                index++;
            }
        }

        return index < tokens.Count ? index : -1;
    }

    private static bool UsesPosixDirectAssignmentGrammar(string command)
    {
        var trimmed = command.Trim();
        var wrapper = ShellWrapperRegex().Match(trimmed);
        if (wrapper.Success)
        {
            var shell = wrapper.Groups["shell"].Value;
            return !string.Equals(
                       shell,
                       "pwsh",
                       StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(
                       shell,
                       "powershell",
                       StringComparison.OrdinalIgnoreCase);
        }

        return CodexPosixShellDisplayRegex().IsMatch(trimmed)
               || !OperatingSystem.IsWindows();
    }

    private static string StripLeadingPosixEnvironmentAssignments(
        string invocation,
        out string[] assignmentNames)
    {
        var names = new List<string>();
        var offset = 0;
        while (offset < invocation.Length
               && IsPosixShellSeparator(invocation[offset]))
        {
            offset++;
        }

        var executableOffset = offset;
        while (TryReadPosixEnvironmentAssignment(
                   invocation,
                   executableOffset,
                   out var end,
                   out var name))
        {
            names.Add(name);
            executableOffset = end;
            while (executableOffset < invocation.Length
                   && IsPosixShellSeparator(invocation[executableOffset]))
            {
                executableOffset++;
            }
        }

        assignmentNames = names.ToArray();
        return names.Count == 0
            ? invocation
            : invocation[executableOffset..];
    }

    private static bool TryReadPosixEnvironmentAssignment(
        string value,
        int offset,
        out int end,
        out string name)
    {
        end = offset;
        name = string.Empty;
        if (offset >= value.Length
            || !(value[offset] is '_' || char.IsAsciiLetter(value[offset])))
        {
            return false;
        }

        var index = offset + 1;
        while (index < value.Length
               && (value[index] is '_'
                   || char.IsAsciiLetterOrDigit(value[index])))
        {
            index++;
        }

        if (index >= value.Length || value[index] != '=')
        {
            return false;
        }

        name = value[offset..index];
        index++;
        var quote = '\0';
        var escaped = false;
        while (index < value.Length)
        {
            var character = value[index];
            if (escaped)
            {
                escaped = false;
                index++;
                continue;
            }

            if (quote == '\0' && IsPosixShellSeparator(character))
            {
                break;
            }

            if (quote != '\'' && character == '\\')
            {
                escaped = true;
                index++;
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = quote == '\0'
                    ? character
                    : quote == character ? '\0' : quote;
            }

            index++;
        }

        if (quote != '\0' || escaped)
        {
            return false;
        }

        end = index;
        return true;
    }

    private static bool ContainsUnquotedNonPosixWhitespace(
        string value,
        char? escapeCharacter)
    {
        var quote = '\0';
        var escaped = false;
        foreach (var character in value)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (quote != '\'' && character == escapeCharacter)
            {
                escaped = true;
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = quote == '\0'
                    ? character
                    : quote == character ? '\0' : quote;
                continue;
            }

            if (quote == '\0'
                && char.IsWhiteSpace(character)
                && !IsPosixShellSeparator(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPosixShellSeparator(char value) =>
        value is ' ' or '\t' or '\n';

    private static bool IsExecutableNamed(string command, string name)
    {
        var tokens = CommandArgumentRegex().Matches(command)
            .Select(static match => Unquote(match.Value))
            .ToArray();
        var executable = FindExecutable(tokens);
        return executable >= 0
               && string.Equals(
                   Path.GetFileName(tokens[executable]),
                   name,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string UnwrapShell(string command)
    {
        var trimmed = command.Trim();
        var match = ShellWrapperRegex().Match(trimmed);
        if (match.Success)
        {
            var body = match.Groups["body"].Value;
            return match.Groups["quote"].Value == "\""
                ? body.Replace("\\\"", "\"", StringComparison.Ordinal)
                : body.Replace("'\\''", "'", StringComparison.Ordinal);
        }

        var display = CodexPosixShellDisplayRegex().Match(trimmed);
        if (!display.Success)
        {
            return trimmed;
        }

        var displayBody = display.Groups["body"].Value;
        const string openingMarker = "'\"'";
        var opening = displayBody.IndexOf(
            openingMarker,
            StringComparison.Ordinal);
        var toolDelimiter = displayBody.IndexOf(
            " -- ",
            StringComparison.Ordinal);
        if (opening < 0
            || toolDelimiter < 0
            || opening <= toolDelimiter + " -- ".Length
            || displayBody.IndexOf(
                openingMarker,
                opening + openingMarker.Length,
                StringComparison.Ordinal) >= 0)
        {
            return trimmed;
        }

        var closing = displayBody.IndexOf(
            '\'',
            opening + openingMarker.Length);
        if (closing < 0)
        {
            return trimmed;
        }

        return string.Concat(
            displayBody[..opening],
            "\"",
            displayBody[(opening + openingMarker.Length)..closing],
            "\"",
            displayBody[(closing + 1)..]);
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

    private static bool ContainsUnquotedControlOperator(
        string command,
        char? escapeCharacter)
    {
        char quote = '\0';
        var escaped = false;
        foreach (var character in command)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (quote != '\''
                && character == escapeCharacter)
            {
                escaped = true;
                continue;
            }

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

        return quote != '\0' || escaped;
    }

    private static char? GetControlEscapeCharacter(string command)
    {
        var match = ShellWrapperRegex().Match(command.Trim());
        if (!match.Success)
        {
            return OperatingSystem.IsWindows() ? null : '\\';
        }

        var shell = match.Groups["shell"].Value;
        return string.Equals(
                   shell,
                   "pwsh",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   shell,
                   "powershell",
                   StringComparison.OrdinalIgnoreCase)
            ? '`'
            : '\\';
    }

    private static bool IsDnxExecutableName(string value)
    {
        var name = Path.GetFileName(value);
        return string.Equals(
                   name,
                   "dnx",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   name,
                   "dnx.exe",
                   StringComparison.OrdinalIgnoreCase);
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
        "^(?:(?:/[^/\\s]+/)?(?<shell>zsh|bash|sh|pwsh|powershell))\\s+(?:-lc|-c|-Command)\\s+(?<quote>[\"'])(?<body>.*)\\k<quote>\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShellWrapperRegex();

    [GeneratedRegex(
        "^(?:(?:/[^/\\s]+/)?(?:zsh|bash|sh))\\s+(?:-lc|-c)\\s+'(?<body>.*)\"\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodexPosixShellDisplayRegex();

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
        "^[A-Za-z_][A-Za-z0-9_]*=.*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentAssignmentRegex();

    [GeneratedRegex(
        "(?:^|[\\s;'\"|&()])(?:rg|grep|find|fd)(?=$|[\\s;'\"|&()])|(?:^|[\\s;'\"|&()])dnaxi\\s+(?:search|show|outline|context)(?=$|[\\s;'\"|&()])|(?:^|[\\s;'\"|&()])dnx\\s+\\S+[^\\r\\n;|&]*\\s--\\s+(?:search|show|outline|context)(?=$|[\\s;'\"|&()])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceSearchCommandRegex();

    [GeneratedRegex(
        "(?:^|[\\s;'\"|&()])(?:cat|sed|head|tail|type|Get-Content)(?=$|[\\s;'\"|&()])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryReadCommandRegex();

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

internal sealed record CodexBenchmarkDnxInvocation(
    string Route,
    string? Target,
    string SelectorKind,
    string? SelectorValue,
    bool IncludeTests,
    bool IncludeGenerated,
    IReadOnlyList<string> Arguments);

internal sealed record CodexBenchmarkTaskRouteStep(
    int Sequence,
    bool Succeeded,
    CodexBenchmarkDnxInvocation Invocation);

internal sealed record CodexBenchmarkTaskRouteEvidence(
    bool Exact,
    bool Successful,
    IReadOnlyList<CodexBenchmarkTaskRouteStep> Steps)
{
    public static CodexBenchmarkTaskRouteEvidence None { get; } =
        new(false, false, Array.Empty<CodexBenchmarkTaskRouteStep>());
}
