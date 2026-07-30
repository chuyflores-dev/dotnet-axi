using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetAxi.Testing;

internal static class FixtureManifestLoader
{
    private const string SupportedSchema = "dotnet-axi/fixture/v1";

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions ManifestOptions =
        new(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

    private static readonly JsonSerializerOptions GeneratedJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

    public static async ValueTask<FixtureMaterializationPlan> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var fullManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifestPath))
        {
            throw new FixtureManifestException(
                $"Fixture manifest '{fullManifestPath}' does not exist.");
        }

        FixtureManifestDocument? document;
        try
        {
            await using var stream = File.OpenRead(fullManifestPath);
            document = await JsonSerializer.DeserializeAsync<FixtureManifestDocument>(
                stream,
                ManifestOptions,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new FixtureManifestException(
                $"Fixture manifest '{fullManifestPath}' is not valid JSON.",
                exception);
        }

        if (document is null)
        {
            throw new FixtureManifestException(
                $"Fixture manifest '{fullManifestPath}' is empty.");
        }

        if (!string.Equals(
                document.Schema,
                SupportedSchema,
                StringComparison.Ordinal))
        {
            throw new FixtureManifestException(
                $"Fixture manifest schema must be '{SupportedSchema}'.");
        }

        var name = ValidateName(document.Name);
        var selectedSdk = ValidateSdk(document.Sdk);
        var identity = new RepositoryFixtureIdentity(
            name,
            document.Seed,
            selectedSdk);
        var manifestDirectory =
            Path.GetDirectoryName(fullManifestPath)
            ?? throw new FixtureManifestException(
                "The fixture manifest must have a parent directory.");
        var files = new List<FixtureMaterializedFile>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in document.Files ?? [])
        {
            var destination = NormalizeRelativePath(
                file.Path,
                "Fixture destination path");
            if (!destinations.Add(destination))
            {
                throw new FixtureManifestException(
                    $"Fixture destination '{destination}' is duplicated.");
            }

            var template = NormalizeRelativePath(
                file.Template,
                "Fixture template path");
            var templatePath = ResolveContainedPath(
                manifestDirectory,
                template,
                "Fixture template");
            EnsureTemplateIsRegularFile(
                manifestDirectory,
                template,
                templatePath);

            var content = await File.ReadAllBytesAsync(
                templatePath,
                cancellationToken);
            if (file.ExpandTokens)
            {
                content = ExpandTokens(content, identity, templatePath);
            }

            files.Add(new FixtureMaterializedFile(destination, content));
        }

        const string globalJsonPath = "global.json";
        if (!destinations.Add(globalJsonPath))
        {
            throw new FixtureManifestException(
                "Fixture templates cannot define global.json; use the sdk manifest field.");
        }

        files.Add(
            new FixtureMaterializedFile(
                globalJsonPath,
                CreateGlobalJson(selectedSdk)));

        return new FixtureMaterializationPlan(
            identity,
            files
                .OrderBy(static file => file.RelativePath, StringComparer.Ordinal)
                .ToArray());
    }

    private static string ValidateName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character)
                  || character is '-' or '_' or '.')))
        {
            throw new FixtureManifestException(
                "Fixture name must contain only ASCII letters, digits, '.', '-', or '_'.");
        }

        return value;
    }

    private static FixtureSdkContext ValidateSdk(FixtureSdkDocument? sdk)
    {
        if (sdk is null
            || string.IsNullOrWhiteSpace(sdk.Version)
            || string.IsNullOrWhiteSpace(sdk.RollForward))
        {
            throw new FixtureManifestException(
                "Fixture sdk.version and sdk.rollForward are required.");
        }

        return new FixtureSdkContext(
            sdk.Version,
            sdk.RollForward,
            sdk.AllowPrerelease);
    }

    private static string NormalizeRelativePath(
        string? value,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FixtureManifestException($"{field} is required.");
        }

        var normalized = value.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains(':')
            || normalized.Any(static character =>
                char.IsControl(character)
                || character is '*' or '?' or '"' or '<' or '>' or '|'))
        {
            throw new FixtureManifestException(
                $"{field} '{value}' is not a portable relative path.");
        }

        var segments = normalized.Split('/');
        if (segments.Any(static segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."))
        {
            throw new FixtureManifestException(
                $"{field} '{value}' cannot contain empty, '.' or '..' segments.");
        }

        return string.Join('/', segments);
    }

    private static string ResolveContainedPath(
        string root,
        string relativePath,
        string field)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(
            Path.Combine(
                fullRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPrefix = fullRoot.EndsWith(
            Path.DirectorySeparatorChar.ToString(),
            StringComparison.Ordinal)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, comparison))
        {
            throw new FixtureManifestException(
                $"{field} '{relativePath}' escapes its root.");
        }

        return candidate;
    }

    private static void EnsureTemplateIsRegularFile(
        string manifestDirectory,
        string relativePath,
        string templatePath)
    {
        if (!File.Exists(templatePath))
        {
            throw new FixtureManifestException(
                $"Fixture template '{templatePath}' does not exist.");
        }

        var candidate = manifestDirectory;
        foreach (var segment in relativePath.Split('/'))
        {
            candidate = Path.Combine(candidate, segment);
            if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint)
                != 0)
            {
                throw new FixtureManifestException(
                    $"Fixture template '{templatePath}' cannot traverse a symbolic link.");
            }
        }
    }

    private static byte[] ExpandTokens(
        byte[] content,
        RepositoryFixtureIdentity identity,
        string templatePath)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException exception)
        {
            throw new FixtureManifestException(
                $"Tokenized fixture template '{templatePath}' must be UTF-8.",
                exception);
        }

        return StrictUtf8.GetBytes(
            text
                .Replace(
                    "{{fixture.name}}",
                    identity.Name,
                    StringComparison.Ordinal)
                .Replace(
                    "{{fixture.seed}}",
                    identity.Seed.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.Ordinal)
                .Replace(
                    "{{sdk.version}}",
                    identity.SelectedSdk.Version,
                    StringComparison.Ordinal));
    }

    private static byte[] CreateGlobalJson(FixtureSdkContext selectedSdk)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                sdk = new
                {
                    version = selectedSdk.Version,
                    rollForward = selectedSdk.RollForward,
                    allowPrerelease = selectedSdk.AllowPrerelease,
                },
            },
            GeneratedJsonOptions);
        return StrictUtf8.GetBytes(
            json.ReplaceLineEndings("\n") + "\n");
    }

    private sealed class FixtureManifestDocument
    {
        public string? Schema { get; init; }

        public string? Name { get; init; }

        public int Seed { get; init; }

        public FixtureSdkDocument? Sdk { get; init; }

        public IReadOnlyList<FixtureFileDocument>? Files { get; init; }
    }

    private sealed class FixtureSdkDocument
    {
        public string? Version { get; init; }

        public string? RollForward { get; init; }

        public bool AllowPrerelease { get; init; }
    }

    private sealed class FixtureFileDocument
    {
        public string? Path { get; init; }

        public string? Template { get; init; }

        public bool ExpandTokens { get; init; }
    }
}

internal sealed record FixtureMaterializationPlan(
    RepositoryFixtureIdentity Identity,
    IReadOnlyList<FixtureMaterializedFile> Files);
