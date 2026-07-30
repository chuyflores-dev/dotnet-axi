namespace DotNetAxi.Contracts;

public enum OperationClassification
{
    Passive,
    Executing,
}

public sealed record OperationPolicy
{
    public OperationPolicy(
        OperationClassification classification,
        bool mayAccessNetwork,
        bool mayExecuteRepositoryCode,
        bool mayWriteArtifacts,
        bool mayWriteMetadata,
        bool mayWriteUserState,
        bool mayWriteSource)
    {
        if (!Enum.IsDefined(classification))
        {
            throw new ArgumentOutOfRangeException(
                nameof(classification),
                classification,
                "The operation classification is not defined.");
        }

        if (classification is OperationClassification.Passive &&
            mayAccessNetwork)
        {
            throw new ArgumentException(
                "Passive operations cannot access the network.",
                nameof(mayAccessNetwork));
        }

        if (classification is OperationClassification.Passive &&
            mayExecuteRepositoryCode)
        {
            throw new ArgumentException(
                "Passive operations cannot execute repository code.",
                nameof(mayExecuteRepositoryCode));
        }

        if (classification is OperationClassification.Executing &&
            !mayExecuteRepositoryCode)
        {
            throw new ArgumentException(
                "Executing operations must declare repository-code execution.",
                nameof(mayExecuteRepositoryCode));
        }

        if (classification is OperationClassification.Passive &&
            mayWriteSource)
        {
            throw new ArgumentException(
                "Source-writing operations must be classified as executing.",
                nameof(mayWriteSource));
        }

        Classification = classification;
        MayAccessNetwork = mayAccessNetwork;
        MayExecuteRepositoryCode = mayExecuteRepositoryCode;
        MayWriteArtifacts = mayWriteArtifacts;
        MayWriteMetadata = mayWriteMetadata;
        MayWriteUserState = mayWriteUserState;
        MayWriteSource = mayWriteSource;
    }

    public static OperationPolicy Passive { get; } = new(
        OperationClassification.Passive,
        mayAccessNetwork: false,
        mayExecuteRepositoryCode: false,
        mayWriteArtifacts: false,
        mayWriteMetadata: false,
        mayWriteUserState: false,
        mayWriteSource: false);

    public OperationClassification Classification { get; }

    public bool MayAccessNetwork { get; }

    public bool MayExecuteRepositoryCode { get; }

    public bool MayWriteArtifacts { get; }

    public bool MayWriteMetadata { get; }

    public bool MayWriteUserState { get; }

    public bool MayWriteSource { get; }
}
