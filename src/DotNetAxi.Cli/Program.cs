using System.Text;

namespace DotNetAxi.Cli;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        Console.OutputEncoding = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        return CliApplication
            .Create(Console.Out, Console.Error)
            .InvokeAsync(args);
    }
}
