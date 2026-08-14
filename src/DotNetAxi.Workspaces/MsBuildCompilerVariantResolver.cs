using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNetAxi.Contracts;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace DotNetAxi.Workspaces;

public sealed record EvaluatedCompilerVariant(
    FileCompilerVariant Variant,
    string? FailureReason,
    IReadOnlySet<string> Sources);

public sealed record CompilerVariantResolution(
    MsBuildRuntimeIdentity? Runtime,
    string? FailureReason,
    IReadOnlyList<EvaluatedCompilerVariant> Variants)
{
    public bool IsAvailable => Runtime is not null && FailureReason is null;
}

/// <summary>
/// Evaluates the selected SDK's effective default configuration and framework
/// declarations without running build targets. Imports and conditions are
/// resolved by MSBuild instead of inferred from project-file text.
/// </summary>
public sealed class MsBuildCompilerVariantResolver
{
    private readonly MsBuildRuntimeRegistrationService _registration;

    public MsBuildCompilerVariantResolver(IDotNetHostResolver hostResolver)
    {
        _registration = new MsBuildRuntimeRegistrationService(
            hostResolver ?? throw new ArgumentNullException(nameof(hostResolver)));
    }

    public CompilerVariantResolution Resolve(
        string workspaceRoot,
        IEnumerable<string> projects,
        CancellationToken cancellationToken = default)
        => Resolve(
            workspaceRoot,
            projects,
            new ProjectGraphEvaluationOptions(),
            cancellationToken);

    public CompilerVariantResolution Resolve(
        string workspaceRoot,
        IEnumerable<string> projects,
        ProjectGraphEvaluationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(options);
        var root = Path.GetFullPath(workspaceRoot);
        var registration = _registration.Register(root, cancellationToken);
        if (!registration.IsAvailable)
        {
            return new CompilerVariantResolution(
                Runtime: null,
                registration.FailureCode ?? "msbuild.unavailable",
                []);
        }

        var variants = projects
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .SelectMany(project => ResolveProject(
                root,
                project,
                options,
                cancellationToken))
            .OrderBy(static variant => variant.Variant.Project, StringComparer.Ordinal)
            .ThenBy(static variant => variant.Variant.Configuration, StringComparer.Ordinal)
            .ThenBy(static variant => variant.Variant.Framework, StringComparer.Ordinal)
            .ToArray();
        return new CompilerVariantResolution(
            registration.Runtime,
            FailureReason: null,
            Array.AsReadOnly(variants));
    }

    private static IReadOnlyList<EvaluatedCompilerVariant> ResolveProject(
        string workspaceRoot,
        string project,
        ProjectGraphEvaluationOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projectPath = Path.GetFullPath(
            project.Replace('/', Path.DirectorySeparatorChar),
            workspaceRoot);
        if (!IsWithin(workspaceRoot, projectPath))
        {
            return [Failed(project, "project.path_escape")];
        }

        var properties = BuildProperties(options);
        try
        {
            using var collection = new ProjectCollection();
            var outer = new Project(
                projectPath,
                properties,
                toolsVersion: null,
                collection,
                ProjectLoadSettings.Default);
            var configuration = options.Configuration
                ?? Optional(outer.GetPropertyValue("Configuration"))
                ?? "Debug";
            properties["Configuration"] = configuration;
            outer.SetGlobalProperty("Configuration", configuration);
            outer.ReevaluateIfNecessary();
            var frameworks = options.Framework is null
                ? Split(outer.GetPropertyValue("TargetFrameworks"))
                : [options.Framework];
            if (frameworks.Count == 0)
            {
                var framework = Optional(
                    outer.GetPropertyValue("TargetFramework"));
                frameworks = framework is null ? [null] : [framework];
            }

            collection.UnloadProject(outer);
            var variants = new List<EvaluatedCompilerVariant>(frameworks.Count);
            foreach (var framework in frameworks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                variants.Add(ResolveVariant(
                    workspaceRoot,
                    projectPath,
                    project,
                    configuration,
                    framework,
                    properties,
                    collection,
                    cancellationToken));
            }

            return Array.AsReadOnly(variants.ToArray());
        }
        catch (InvalidProjectFileException)
        {
            return [Failed(project, "project.invalid")];
        }
        catch (FileNotFoundException)
        {
            return [Failed(project, "project.not_found")];
        }
        catch (DirectoryNotFoundException)
        {
            return [Failed(project, "project.not_found")];
        }
        catch (UnauthorizedAccessException)
        {
            return [Failed(project, "project.unreadable")];
        }
        catch (IOException)
        {
            return [Failed(project, "project.load_failed")];
        }
    }

    private static Dictionary<string, string> BuildProperties(
        ProjectGraphEvaluationOptions options)
    {
        var properties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var property in options.Properties)
        {
            properties[property.Name] = property.Value;
        }

        properties["DesignTimeBuild"] = "true";
        properties["BuildingInsideVisualStudio"] = "true";
        properties["BuildProjectReferences"] = "false";
        properties["SkipCompilerExecution"] = "true";
        properties["ProvideCommandLineArgs"] = "true";
        if (options.Configuration is not null)
        {
            properties["Configuration"] = options.Configuration;
        }

        if (options.Framework is not null)
        {
            properties["TargetFramework"] = options.Framework;
        }

        return properties;
    }

    private static EvaluatedCompilerVariant ResolveVariant(
        string workspaceRoot,
        string projectPath,
        string project,
        string configuration,
        string? framework,
        IReadOnlyDictionary<string, string> properties,
        ProjectCollection collection,
        CancellationToken cancellationToken)
    {
        var variantProperties = new Dictionary<string, string>(
            properties,
            StringComparer.OrdinalIgnoreCase);
        if (framework is not null)
        {
            variantProperties["TargetFramework"] = framework;
        }

        try
        {
            var evaluated = new Project(
                projectPath,
                variantProperties,
                toolsVersion: null,
                collection,
                ProjectLoadSettings.Default);
            var fingerprint = Fingerprint(
                workspaceRoot,
                evaluated,
                cancellationToken);
            return new EvaluatedCompilerVariant(
                new FileCompilerVariant(
                    project,
                    configuration,
                    framework,
                    fingerprint),
                IncompleteProjectReason(evaluated, cancellationToken),
                SourcePaths(workspaceRoot, evaluated));
        }
        catch (InvalidProjectFileException)
        {
            return Failed(project, configuration, framework, "project.invalid");
        }
        catch (FileNotFoundException)
        {
            return Failed(project, configuration, framework, "project.not_found");
        }
        catch (DirectoryNotFoundException)
        {
            return Failed(project, configuration, framework, "project.not_found");
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(project, configuration, framework, "project.unreadable");
        }
        catch (IOException)
        {
            return Failed(project, configuration, framework, "project.load_failed");
        }
    }

    private static EvaluatedCompilerVariant Failed(
        string project,
        string reason) =>
        Failed(
            project,
            configuration: null,
            framework: null,
            reason);

    private static EvaluatedCompilerVariant Failed(
        string project,
        string? configuration,
        string? framework,
        string reason) =>
        new(
            new FileCompilerVariant(
                project,
                configuration,
                framework,
                contextFingerprint: project),
            reason,
            new HashSet<string>(StringComparer.Ordinal));

    private static IReadOnlySet<string> SourcePaths(
        string workspaceRoot,
        Project project)
    {
        var pathResolver = new WorkspacePathResolver(
            workspaceRoot,
            workspaceRoot);
        return project.GetItems("Compile")
            .Select(static item => item.GetMetadataValue("FullPath"))
            .Where(static path => Path.IsPathFullyQualified(path))
            .Select(path => pathResolver.NormalizeOutput(path).Path)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<string?> Split(string value) =>
        Array.AsReadOnly<string?>(value
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Cast<string?>()
            .ToArray());

    private static string Fingerprint(
        string workspaceRoot,
        Project project,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "dotnet-axi/msbuild-compiler-context/v1");
        var paths = project.Imports
            .Select(static import => import.ImportedProject.FullPath)
            .Append(project.FullPath)
            .Distinct(PathComparer())
            .Order(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, FingerprintPath(workspaceRoot, path));
            if (File.Exists(path))
            {
                Append(hash, Convert.ToHexStringLower(
                    SHA256.HashData(File.ReadAllBytes(path))));
            }
            else
            {
                Append(hash, "missing");
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string? IncompleteProjectReason(
        Project project,
        CancellationToken cancellationToken)
    {
        var projectDirectory = Path.GetDirectoryName(project.FullPath)!;
        foreach (var reference in project.GetItems("ProjectReference"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = reference.EvaluatedInclude
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (!File.Exists(Path.GetFullPath(normalized, projectDirectory)))
            {
                return "project.reference_not_found";
            }
        }

        foreach (var reference in project.GetItems("Reference"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hintPath = reference.GetMetadataValue("HintPath").Trim();
            if (hintPath.Length == 0)
            {
                continue;
            }

            var normalized = hintPath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (!File.Exists(Path.GetFullPath(normalized, projectDirectory)))
            {
                return "metadata.missing";
            }
        }

        if (project.GetItems("PackageReference").Count == 0)
        {
            return null;
        }

        var assetsPath = Path.Combine(
            projectDirectory,
            "obj",
            "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            return "metadata.missing";
        }

        try
        {
            using var assets = JsonDocument.Parse(File.ReadAllBytes(assetsPath));
            var framework = Optional(project.GetPropertyValue("TargetFramework"));
            if (framework is null
                || !assets.RootElement.TryGetProperty("targets", out var targets)
                || !targets.EnumerateObject().Any(target =>
                    target.Name.Equals(framework, StringComparison.Ordinal)
                    || target.Name.StartsWith(
                        framework + "/",
                        StringComparison.Ordinal)))
            {
                return "metadata.missing";
            }
        }
        catch (JsonException)
        {
            return "metadata.missing";
        }
        catch (IOException)
        {
            return "metadata.missing";
        }

        return null;
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string FingerprintPath(string workspaceRoot, string path) =>
        IsWithin(workspaceRoot, path)
            ? Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/')
            : Path.GetFileName(path);

    private static string? Optional(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathFullyQualified(relative)
            && relative != ".."
            && !relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            && !relative.StartsWith(
                ".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal);
    }
}
