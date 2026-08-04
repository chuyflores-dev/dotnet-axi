namespace DotNetAxi.Cli;

internal sealed class CommandUsageException(string code, string message, string correction)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public string Correction { get; } = correction;
}
