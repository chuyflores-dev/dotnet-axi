namespace DotNetAxi.Testing;

internal static class AgentBenchmarkFactSet
{
    public static IReadOnlyList<string> Normalize(
        string answer,
        string normalizer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var normalized = answer.ReplaceLineEndings("\n").TrimEnd('\n');
        var lines = normalized.Length == 0
            ? []
            : normalized.Split('\n');
        return normalizer switch
        {
            "ordinal-lines/v1" => lines
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            "ordinal-sequence/v1" => lines,
            _ => throw new AgentBenchmarkException(
                $"Unsupported fact normalizer '{normalizer}'."),
        };
    }

    public static bool EqualsExpected(
        string answer,
        IReadOnlyList<string> expectedFacts,
        string normalizer) =>
        Normalize(answer, normalizer).SequenceEqual(
            expectedFacts,
            StringComparer.Ordinal);

    public static bool ContainsOnlyExpected(
        string answer,
        IReadOnlyList<string> expectedFacts,
        string normalizer) =>
        Normalize(answer, normalizer).All(fact =>
            expectedFacts.Contains(fact, StringComparer.Ordinal));
}
