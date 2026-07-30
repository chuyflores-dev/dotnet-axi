namespace DotNetAxi.Cli.Tests;

public sealed class EntryPointSmokeTests
{
    [Fact]
    public void Entry_point_completes_without_discovery_or_process_execution()
    {
        var exitCode = Cli.Program.Main([]);

        Assert.Equal(0, exitCode);
    }
}
