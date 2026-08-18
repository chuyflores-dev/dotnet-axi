using DotNetAxi.Axi;

return Run(args, Console.Out, Console.Error);

static int Run(
    IReadOnlyList<string> arguments,
    TextWriter output,
    TextWriter error)
{
    try
    {
        var options = Parse(arguments);

        if (options.SemanticRelationshipVersion is not null)
        {
            AgentSkillDocuments.WriteSemanticRelationships(
                options.OutputRoot!,
                options.SemanticRelationshipVersion);
            foreach (var document in
                     AgentSkillDocuments.RenderSemanticRelationships(
                         options.SemanticRelationshipVersion))
            {
                output.WriteLine($"generated: {document.RelativePath}");
            }

            return 0;
        }

        var repositoryRoot = options.RepositoryRoot
            ?? FindRepositoryRoot(Directory.GetCurrentDirectory());

        if (options.Write)
        {
            AgentSkillDocuments.Write(repositoryRoot);
            foreach (var document in AgentSkillDocuments.Render())
            {
                output.WriteLine($"generated: {document.RelativePath}");
            }

            return 0;
        }

        var stale = AgentSkillDocuments.FindStale(repositoryRoot);
        if (stale.Count == 0)
        {
            output.WriteLine("Agent Skill generated content is current.");
            return 0;
        }

        foreach (var document in stale)
        {
            error.WriteLine(
                $"stale: {document.RelativePath} ({StateText(document.State)})");
        }

        error.WriteLine(
            "Run the generator with --write from the repository root.");
        return 1;
    }
    catch (GeneratorUsageException exception)
    {
        error.WriteLine(exception.Message);
        error.WriteLine(
            "Usage: dotnet run --project eng/DotNetAxi.AgentSkillGenerator -- ((--check|--write) [--repository-root <path>]|--write-semantic-relationships <version> --output-root <path>)");
        return 2;
    }
    catch (Exception exception)
    {
        error.WriteLine($"Agent Skill generation failed: {exception.Message}");
        return 1;
    }
}

static GeneratorOptions Parse(IReadOnlyList<string> arguments)
{
    var check = false;
    var write = false;
    string? semanticRelationshipVersion = null;
    string? repositoryRoot = null;
    string? outputRoot = null;

    for (var index = 0; index < arguments.Count; index++)
    {
        switch (arguments[index])
        {
            case "--check":
                check = true;
                break;
            case "--write":
                write = true;
                break;
            case "--write-semantic-relationships":
                if (++index >= arguments.Count
                    || string.IsNullOrWhiteSpace(arguments[index]))
                {
                    throw new GeneratorUsageException(
                        "--write-semantic-relationships requires an exact version.");
                }

                semanticRelationshipVersion = arguments[index];
                break;
            case "--repository-root":
                if (++index >= arguments.Count
                    || string.IsNullOrWhiteSpace(arguments[index]))
                {
                    throw new GeneratorUsageException(
                        "--repository-root requires a path.");
                }

                repositoryRoot = arguments[index];
                break;
            case "--output-root":
                if (++index >= arguments.Count
                    || string.IsNullOrWhiteSpace(arguments[index]))
                {
                    throw new GeneratorUsageException(
                        "--output-root requires a path.");
                }

                outputRoot = arguments[index];
                break;
            default:
                throw new GeneratorUsageException(
                    $"Unknown argument '{arguments[index]}'.");
        }
    }

    var modeCount = (check ? 1 : 0)
        + (write ? 1 : 0)
        + (semanticRelationshipVersion is null ? 0 : 1);
    if (modeCount != 1)
    {
        throw new GeneratorUsageException(
            "Specify exactly one generation mode.");
    }

    if (semanticRelationshipVersion is not null)
    {
        if (outputRoot is null)
        {
            throw new GeneratorUsageException(
                "--write-semantic-relationships requires --output-root.");
        }

        if (repositoryRoot is not null)
        {
            throw new GeneratorUsageException(
                "--repository-root is not valid for semantic candidate output.");
        }
    }
    else if (outputRoot is not null)
    {
        throw new GeneratorUsageException(
            "--output-root is valid only for semantic candidate output.");
    }

    return new GeneratorOptions(
        write,
        semanticRelationshipVersion,
        repositoryRoot,
        outputRoot);
}

static string FindRepositoryRoot(string startPath)
{
    for (var directory = new DirectoryInfo(Path.GetFullPath(startPath));
         directory is not null;
         directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "dotnet-axi.slnx")))
        {
            return directory.FullName;
        }
    }

    throw new GeneratorUsageException(
        $"Could not find dotnet-axi.slnx above '{startPath}'. Use --repository-root.");
}

static string StateText(GeneratedDocumentState state) =>
    state switch
    {
        GeneratedDocumentState.Missing => "missing",
        GeneratedDocumentState.Different => "different",
        _ => throw new ArgumentOutOfRangeException(
            nameof(state),
            state,
            "The generated document state is not defined."),
    };

internal sealed record GeneratorOptions(
    bool Write,
    string? SemanticRelationshipVersion,
    string? RepositoryRoot,
    string? OutputRoot);

internal sealed class GeneratorUsageException : Exception
{
    public GeneratorUsageException(string message)
        : base(message)
    {
    }
}
