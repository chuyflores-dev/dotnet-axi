using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

internal static class VersionResult
{
    public static ICommandResult Create(string toolVersion) =>
        CommandResult<VersionPayload>.Success(
            "version",
            new VersionPayload(
                "dotnet-axi",
                toolVersion,
                OutputSchema.Current));

    private sealed record VersionPayload(
        string Tool,
        string ToolVersion,
        string OutputSchema);
}
