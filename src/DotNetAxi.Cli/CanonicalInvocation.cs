using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

internal static class CanonicalInvocation
{
    private static string PackageReference => $"dnaxi@{ToolVersion.Current}";

    public static string OneShot(string installedInvocation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installedInvocation);
        if (installedInvocation is not "dnaxi" &&
            !installedInvocation.StartsWith("dnaxi ", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An installed dnaxi invocation is required.",
                nameof(installedInvocation));
        }

        return $"dnx {PackageReference} --verbosity quiet --" +
            installedInvocation["dnaxi".Length..];
    }

    public static ResultSuggestion OneShot(ResultSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        if (!string.Equals(
                suggestion.Command,
                "dnaxi",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A dnaxi suggestion is required.",
                nameof(suggestion));
        }

        return new ResultSuggestion(
            "dnx",
            [
                PackageReference,
                "--verbosity",
                "quiet",
                "--",
                .. suggestion.Arguments,
            ]);
    }
}
