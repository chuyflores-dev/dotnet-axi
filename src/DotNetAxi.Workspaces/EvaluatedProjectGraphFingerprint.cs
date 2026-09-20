using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DotNetAxi.Workspaces;

/// <summary>
/// Produces a deterministic identity for the evaluated project-graph evidence
/// that commands expose to callers.
/// </summary>
public static class EvaluatedProjectGraphFingerprint
{
    public static string Create(EvaluatedProjectGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/evaluated-project-graph/v1");
        Append(hash, graph.Selection.Kind.ToString());
        Append(hash, graph.Selection.Path);
        Append(hash, graph.Selection.Source.ToString());
        Append(hash, graph.Completeness.ToString());
        foreach (var property in graph.GlobalProperties
                     .OrderBy(static property => property.Name, StringComparer.Ordinal)
                     .ThenBy(static property => property.Value, StringComparer.Ordinal))
        {
            Append(hash, property.Name);
            Append(hash, property.Value);
        }

        Append(hash, graph.Runtime?.SdkVersion ?? string.Empty);
        Append(hash, graph.Runtime?.MsBuildVersion ?? string.Empty);
        foreach (var project in graph.Projects.OrderBy(static project => project.Path, StringComparer.Ordinal))
        {
            Append(hash, project.Path);
            Append(hash, project.IsExternal.ToString());
            Append(hash, project.Configuration ?? string.Empty);
            Append(hash, project.Framework ?? string.Empty);
            Append(hash, project.State.ToString());
            AppendFailures(hash, project.Failures);
        }

        foreach (var dependency in graph.Dependencies
                     .OrderBy(static dependency => dependency.Project, StringComparer.Ordinal)
                     .ThenBy(static dependency => dependency.Dependency, StringComparer.Ordinal)
                     .ThenBy(static dependency => dependency.Configuration, StringComparer.Ordinal)
                     .ThenBy(static dependency => dependency.Framework, StringComparer.Ordinal)
                     .ThenBy(static dependency => dependency.DependencyConfiguration, StringComparer.Ordinal)
                     .ThenBy(static dependency => dependency.DependencyFramework, StringComparer.Ordinal))
        {
            Append(hash, dependency.Project);
            Append(hash, dependency.Dependency);
            Append(hash, dependency.Configuration ?? string.Empty);
            Append(hash, dependency.Framework ?? string.Empty);
            Append(hash, dependency.DependencyConfiguration ?? string.Empty);
            Append(hash, dependency.DependencyFramework ?? string.Empty);
        }

        foreach (var dependency in graph.PackageDependencies
                     .OrderBy(static dependency => dependency.Project, StringComparer.Ordinal)
                     .ThenBy(static dependency => dependency.PackageId, StringComparer.Ordinal)
                     .ThenBy(static dependency => dependency.Version, StringComparer.Ordinal)
                     .ThenBy(static dependency => dependency.Configuration, StringComparer.Ordinal)
                     .ThenBy(static dependency => dependency.Framework, StringComparer.Ordinal))
        {
            Append(hash, dependency.Project);
            Append(hash, dependency.PackageId);
            Append(hash, dependency.Version ?? string.Empty);
            Append(hash, dependency.Configuration ?? string.Empty);
            Append(hash, dependency.Framework ?? string.Empty);
        }

        foreach (var variant in graph.CoverageEvidence
                     .OrderBy(static variant => variant.Project, StringComparer.Ordinal)
                     .ThenBy(static variant => variant.Configuration, StringComparer.Ordinal)
                     .ThenBy(static variant => variant.Framework, StringComparer.Ordinal)
                     .ThenBy(static variant => variant.IsOuterBuild)
                     .ThenBy(static variant => variant.State))
        {
            Append(hash, variant.Project);
            Append(hash, variant.Configuration ?? string.Empty);
            Append(hash, variant.Framework ?? string.Empty);
            foreach (var framework in variant.DeclaredFrameworks.Order(StringComparer.Ordinal))
            {
                Append(hash, framework);
            }

            Append(hash, variant.Language ?? string.Empty);
            Append(hash, variant.IsSdkStyle?.ToString() ?? string.Empty);
            Append(hash, variant.IsOuterBuild.ToString());
            Append(hash, variant.State.ToString());
            AppendFailures(hash, variant.Failures);
        }

        AppendFailures(hash, graph.Failures);
        return "gpg_" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendFailures(
        IncrementalHash hash,
        IEnumerable<ProjectEvaluationFailure> failures)
    {
        foreach (var failure in failures
                     .OrderBy(static failure => failure.Reason)
                     .ThenBy(static failure => failure.AuthorityCode, StringComparer.Ordinal))
        {
            Append(hash, failure.Reason.ToString());
            Append(hash, failure.AuthorityCode ?? string.Empty);
        }
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
