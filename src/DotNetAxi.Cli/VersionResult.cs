using DotNetAxi.Contracts;

namespace DotNetAxi.Cli;

internal static class VersionResult
{
    public static ICommandResult Create(
        string toolVersion,
        CapabilityReport? capabilities = null) =>
        CommandResult<VersionPayload>.Success(
            "version",
            new VersionPayload(
                "dotnet-axi",
                toolVersion,
                OutputSchema.Current,
                capabilities));

    private sealed record VersionPayload(
        string Tool,
        string ToolVersion,
        string OutputSchema,
        CapabilityReport? Capabilities);
}
