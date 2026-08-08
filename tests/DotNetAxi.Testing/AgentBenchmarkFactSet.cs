namespace DotNetAxi.Testing;

internal static class AgentBenchmarkFactSet
{
    public static IReadOnlyList<string> Normalize(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var normalized = answer.ReplaceLineEndings("\n").TrimEnd('\n');
        return normalized.Length == 0
            ? []
            : normalized
                .Split('\n')
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
    }

    public static bool EqualsExpected(
        string answer,
        IReadOnlyList<string> expectedFacts) =>
        Normalize(answer).SequenceEqual(
            expectedFacts,
            StringComparer.Ordinal);

    public static bool ContainsOnlyExpected(
        string answer,
        IReadOnlyList<string> expectedFacts) =>
        Normalize(answer).All(fact =>
            expectedFacts.Contains(fact, StringComparer.Ordinal));
}
