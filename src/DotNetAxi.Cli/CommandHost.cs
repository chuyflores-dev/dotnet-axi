using System.CommandLine;

namespace DotNetAxi.Cli;

public sealed class CommandHost
{
    private readonly RootCommand _rootCommand;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly ParserConfiguration _parserConfiguration = new()
    {
        EnablePosixBundling = false,
        ResponseFileTokenReplacer = null,
    };

    public CommandHost(
        RootCommand rootCommand,
        TextWriter output,
        TextWriter error)
    {
        _rootCommand = rootCommand
            ?? throw new ArgumentNullException(nameof(rootCommand));
        _output = output
            ?? throw new ArgumentNullException(nameof(output));
        _error = error
            ?? throw new ArgumentNullException(nameof(error));
    }

    public ParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return _rootCommand.Parse(args, _parserConfiguration);
    }

    public Task<int> InvokeAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var parseResult = Parse(args);
        var invocationConfiguration = new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = false,
            Output = _output,
            Error = _error,
        };

        return parseResult.InvokeAsync(invocationConfiguration, cancellationToken);
    }
}
