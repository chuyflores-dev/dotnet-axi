namespace DotNetAxi.Cli.Tests;

public sealed class EntryPointSmokeTests
{
    [Fact]
    public async Task Entry_point_completes_without_discovery_or_process_execution()
    {
        var exitCode = await Cli.Program.Main([]);

        Assert.Equal(0, exitCode);
    }
}
