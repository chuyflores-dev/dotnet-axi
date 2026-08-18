using DotNetAxi.Workspaces;

namespace DotNetAxi.Roslyn;

internal sealed record SemanticRelationshipProjectScope(
    IReadOnlyList<string> Default,
    IReadOnlyList<string> Complete)
{
    public static SemanticRelationshipProjectScope Resolve(
        EvaluatedProjectGraph graph,
        IEnumerable<string> targetProjects,
        bool includeTests)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(targetProjects);

        var comparer = PathComparer();
        var seeds = targetProjects.ToHashSet(comparer);
        var complete = new HashSet<string>(seeds, comparer);
        var queue = new Queue<string>(seeds);
        while (queue.TryDequeue(out var dependency))
        {
            foreach (var project in graph.Dependencies
                         .Where(edge => comparer.Equals(
                             edge.Dependency,
                             dependency))
                         .Select(static edge => edge.Project)
                         .Where(project => includeTests
                             || !IsTestProject(project)))
            {
                if (complete.Add(project))
                {
                    queue.Enqueue(project);
                }
            }
        }

        var direct = new HashSet<string>(seeds, comparer);
        foreach (var project in graph.Dependencies
                     .Where(edge => seeds.Contains(edge.Dependency))
                     .Select(static edge => edge.Project)
                     .Where(project => includeTests
                         || !IsTestProject(project)))
        {
            direct.Add(project);
        }

        return new SemanticRelationshipProjectScope(
            direct.Order(StringComparer.Ordinal).ToArray(),
            complete.Order(StringComparer.Ordinal).ToArray());
    }

    private static bool IsTestProject(string project) =>
        project
            .Split(
                ['/', '\\', '.', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(static token =>
                token.Equals("test", StringComparison.OrdinalIgnoreCase)
                || token.Equals("tests", StringComparison.OrdinalIgnoreCase)
                || token.EndsWith("Tests", StringComparison.OrdinalIgnoreCase));

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
