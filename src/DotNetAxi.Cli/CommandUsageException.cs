namespace DotNetAxi.Cli;

internal sealed class CommandUsageException(
    string code,
    string message,
    string correction,
    IEnumerable<string>? candidatePaths = null)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public string Correction { get; } = correction;
    public IReadOnlyList<string> CandidatePaths { get; } = Array.AsReadOnly(
        (candidatePaths ?? []).ToArray());
}
