namespace DotNetAxi.Cli;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return CliApplication
            .Create(Console.Out, Console.Error)
            .InvokeAsync(args);
    }
}
