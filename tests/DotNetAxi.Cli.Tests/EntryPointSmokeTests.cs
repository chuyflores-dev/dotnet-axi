namespace DotNetAxi.Cli.Tests;

public sealed class EntryPointSmokeTests
{
    [Fact]
    public async Task Entry_point_completes_the_home_view()
    {
        var exitCode = await Cli.Program.Main([]);

        Assert.Equal(0, exitCode);
    }
}
