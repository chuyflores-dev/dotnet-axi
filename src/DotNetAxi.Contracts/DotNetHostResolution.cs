namespace DotNetAxi.Contracts;

public enum DotNetHostFailureReason
{
    HostNotFound,
    HostUnsupported,
    SdkProbeTimedOut,
    SdkProbeFailed,
    SdkUnavailable,
    SdkUnsupported,
    SdkSelectionInvalid,
    MsBuildUnavailable,
    ProcessPolicyDenied,
}

public enum DotNetHostCompatibility
{
    Supported,
    Unverified,
}

public sealed record DotNetHostFailure(
    DotNetHostFailureReason Reason,
    string Code,
    string Correction);

public sealed record SelectedDotNetSdk(
    string Version,
    string SdkPath,
    string MsBuildPath,
    DotNetHostCompatibility Compatibility);

public sealed record DotNetHostResolution(
    string? ExecutablePath,
    SelectedDotNetSdk? Sdk,
    DotNetHostFailure? Failure)
{
    public bool IsResolved => ExecutablePath is not null
        && Sdk is not null
        && Failure is null;
}

public sealed class DotNetHostResolutionRequest
{
    public DotNetHostResolutionRequest(
        string workspaceRoot,
        string? explicitHostPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!Path.IsPathFullyQualified(workspaceRoot))
        {
            throw new ArgumentException(
                "The workspace root must be fully qualified.",
                nameof(workspaceRoot));
        }

        if (explicitHostPath is not null
            && (!Path.IsPathFullyQualified(explicitHostPath)
                || string.IsNullOrWhiteSpace(explicitHostPath)))
        {
            throw new ArgumentException(
                "An explicit dotnet host path must be fully qualified.",
                nameof(explicitHostPath));
        }

        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        ExplicitHostPath = explicitHostPath is null
            ? null
            : Path.GetFullPath(explicitHostPath);
    }

    public string WorkspaceRoot { get; }

    public string? ExplicitHostPath { get; }
}

public interface IDotNetHostResolver
{
    ValueTask<DotNetHostResolution> ResolveAsync(
        DotNetHostResolutionRequest request,
        CancellationToken cancellationToken = default);
}
