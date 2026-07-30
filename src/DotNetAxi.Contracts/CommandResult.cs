namespace DotNetAxi.Contracts;

public static class OutputSchema
{
    public const string Current = "dotnet-axi/v1";
}

public enum ResultStatus
{
    Success,
    Partial,
    Failed,
    Cancelled,
}

public interface ICommandResult
{
    string Schema { get; }

    string Command { get; }

    ResultStatus Status { get; }

    object? Payload { get; }

    Evidence? Evidence { get; }

    IReadOnlyList<ResultSuggestion> Suggestions { get; }

    IReadOnlyList<ResultError> Errors { get; }
}

public sealed class CommandResult<TPayload> : ICommandResult
{
    private CommandResult(
        string command,
        ResultStatus status,
        TPayload? payload,
        Evidence? evidence,
        IEnumerable<ResultSuggestion>? suggestions,
        IEnumerable<ResultError>? errors)
    {
        Command = ContractGuards.RequiredText(command, nameof(command));
        Status = status;
        Payload = payload;
        Evidence = evidence;
        Suggestions = ContractGuards.Copy(suggestions);
        Errors = ContractGuards.Copy(errors);

        if (status is ResultStatus.Success or ResultStatus.Partial &&
            payload is null)
        {
            throw new ArgumentNullException(
                nameof(payload),
                $"{status} results require a payload.");
        }

        if (status is ResultStatus.Success && Errors.Count > 0)
        {
            throw new ArgumentException(
                "Successful results cannot contain errors.",
                nameof(errors));
        }

        if (status is ResultStatus.Failed && Errors.Count == 0)
        {
            throw new ArgumentException(
                "Failed results require at least one error.",
                nameof(errors));
        }
    }

    public string Schema => OutputSchema.Current;

    public string Command { get; }

    public ResultStatus Status { get; }

    public TPayload? Payload { get; }

    object? ICommandResult.Payload => Payload;

    public Evidence? Evidence { get; }

    public IReadOnlyList<ResultSuggestion> Suggestions { get; }

    public IReadOnlyList<ResultError> Errors { get; }

    public static CommandResult<TPayload> Success(
        string command,
        TPayload payload,
        Evidence? evidence = null,
        IEnumerable<ResultSuggestion>? suggestions = null) =>
        new(
            command,
            ResultStatus.Success,
            payload,
            evidence,
            suggestions,
            errors: null);

    public static CommandResult<TPayload> Partial(
        string command,
        TPayload payload,
        Evidence? evidence = null,
        IEnumerable<ResultSuggestion>? suggestions = null,
        IEnumerable<ResultError>? errors = null) =>
        new(
            command,
            ResultStatus.Partial,
            payload,
            evidence,
            suggestions,
            errors);

    public static CommandResult<TPayload> Failed(
        string command,
        IEnumerable<ResultError> errors,
        TPayload? payload = default,
        Evidence? evidence = null,
        IEnumerable<ResultSuggestion>? suggestions = null) =>
        new(
            command,
            ResultStatus.Failed,
            payload,
            evidence,
            suggestions,
            errors);

    public static CommandResult<TPayload> Cancelled(
        string command,
        TPayload? payload = default,
        Evidence? evidence = null,
        IEnumerable<ResultSuggestion>? suggestions = null,
        IEnumerable<ResultError>? errors = null) =>
        new(
            command,
            ResultStatus.Cancelled,
            payload,
            evidence,
            suggestions,
            errors);
}

public sealed record ResultSuggestion
{
    public ResultSuggestion(
        string command,
        IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Command = ContractGuards.RequiredText(command, nameof(command));
        Arguments = Array.AsReadOnly(
            ContractGuards
                .Copy(arguments)
                .Select(argument => ContractGuards.RequiredText(
                    argument,
                    nameof(arguments)))
                .ToArray());
    }

    public string Command { get; }

    public IReadOnlyList<string> Arguments { get; }
}

public sealed record ResultError
{
    public ResultError(string code, string message, string correction)
    {
        Code = ContractGuards.RequiredText(code, nameof(code));
        Message = ContractGuards.RequiredText(message, nameof(message));
        Correction = ContractGuards.RequiredText(correction, nameof(correction));
    }

    public string Code { get; }

    public string Message { get; }

    public string Correction { get; }
}
