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
            "Usage: dotnet run --project eng/DotNetAxi.AgentSkillGenerator -- (--check|--write) [--repository-root <path>]");
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
    string? repositoryRoot = null;

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
            case "--repository-root":
                if (++index >= arguments.Count
                    || string.IsNullOrWhiteSpace(arguments[index]))
                {
                    throw new GeneratorUsageException(
                        "--repository-root requires a path.");
                }

                repositoryRoot = arguments[index];
                break;
            default:
                throw new GeneratorUsageException(
                    $"Unknown argument '{arguments[index]}'.");
        }
    }

    if (check == write)
    {
        throw new GeneratorUsageException(
            "Specify exactly one of --check or --write.");
    }

    return new GeneratorOptions(write, repositoryRoot);
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
    string? RepositoryRoot);

internal sealed class GeneratorUsageException : Exception
{
    public GeneratorUsageException(string message)
        : base(message)
    {
    }
}
