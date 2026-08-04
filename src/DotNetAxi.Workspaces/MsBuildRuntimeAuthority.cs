using System.Diagnostics;
using System.Reflection;
using DotNetAxi.Contracts;
using Microsoft.Build.Locator;

namespace DotNetAxi.Workspaces;

internal sealed record DotNetSdkSelection(
    SelectedDotNetSdk? Sdk,
    string? FailureCode)
{
    public bool IsSelected => Sdk is not null && FailureCode is null;
}

internal interface IDotNetSdkSelector
{
    DotNetSdkSelection Select(
        string workspaceRoot,
        CancellationToken cancellationToken);
}

internal sealed class DotNetSdkSelector : IDotNetSdkSelector
{
    private readonly IDotNetHostResolver _resolver;

    internal DotNetSdkSelector(IDotNetHostResolver resolver)
    {
        _resolver = resolver
            ?? throw new ArgumentNullException(nameof(resolver));
    }

    public DotNetSdkSelection Select(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var resolution = _resolver.ResolveAsync(
                new DotNetHostResolutionRequest(workspaceRoot),
                cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return resolution.IsResolved
            ? new DotNetSdkSelection(resolution.Sdk, null)
            : new DotNetSdkSelection(
                null,
                resolution.Failure?.Code ?? "sdk.selection_failed");
    }
}

internal sealed record MsBuildAuthorityResult(
    MsBuildRuntimeIdentity? Runtime,
    ProjectEvaluationFailure? Failure)
{
    public bool IsAvailable => Runtime is not null && Failure is null;
}

internal interface IMsBuildRuntimeAuthority
{
    MsBuildAuthorityResult ResolveAndRegister(
        string workspaceRoot,
        CancellationToken cancellationToken);
}

internal enum MsBuildRegistrationDecision
{
    Register,
    UseRegistered,
    Mismatch,
}

internal interface IMsBuildRuntimeRegistrar
{
    bool IsRegistered { get; }

    string? LoadedMsBuildPath();

    void RegisterMsBuildPath(string path);
}

internal sealed class SystemMsBuildRuntimeRegistrar : IMsBuildRuntimeRegistrar
{
    public bool IsRegistered => MSBuildLocator.IsRegistered;

    public string? LoadedMsBuildPath()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(static candidate => candidate.GetName().Name?.Equals(
                "Microsoft.Build",
                StringComparison.Ordinal) is true);
        return assembly is null || string.IsNullOrWhiteSpace(assembly.Location)
            ? null
            : Path.GetDirectoryName(assembly.Location);
    }

    public void RegisterMsBuildPath(string path) =>
        MSBuildLocator.RegisterMSBuildPath(path);
}

internal sealed class MsBuildRuntimeAuthority : IMsBuildRuntimeAuthority
{
    private static readonly object RegistrationGate = new();
    private static readonly Version CompileContractVersion = new(15, 1, 0, 0);
    private static string? _registeredMsBuildPath;
    private readonly IDotNetSdkSelector _selector;
    private readonly IMsBuildRuntimeRegistrar _registrar;

    internal MsBuildRuntimeAuthority(IDotNetSdkSelector selector)
        : this(selector, new SystemMsBuildRuntimeRegistrar())
    {
    }

    internal MsBuildRuntimeAuthority(
        IDotNetSdkSelector selector,
        IMsBuildRuntimeRegistrar registrar)
    {
        _selector = selector
            ?? throw new ArgumentNullException(nameof(selector));
        _registrar = registrar
            ?? throw new ArgumentNullException(nameof(registrar));
    }

    public MsBuildAuthorityResult ResolveAndRegister(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var selection = _selector.Select(workspaceRoot, cancellationToken);
        if (!selection.IsSelected)
        {
            return Failure(
                ProjectEvaluationFailureReason.MsBuildUnavailable,
                selection.FailureCode ?? "sdk.selection_failed");
        }

        var sdk = selection.Sdk!;
        var msBuildAssemblyPath = sdk.MsBuildPath;
        if (!File.Exists(msBuildAssemblyPath))
        {
            return Failure(
                ProjectEvaluationFailureReason.MsBuildUnavailable,
                "msbuild.selected_instance_missing");
        }

        AssemblyName assemblyName;
        try
        {
            assemblyName = AssemblyName.GetAssemblyName(msBuildAssemblyPath);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or IOException
                  or UnauthorizedAccessException
                  or BadImageFormatException)
        {
            return Failure(
                ProjectEvaluationFailureReason.MsBuildIncompatible,
                "msbuild.contract_unreadable");
        }

        if (assemblyName.Version != CompileContractVersion)
        {
            return Failure(
                ProjectEvaluationFailureReason.MsBuildIncompatible,
                "msbuild.contract_mismatch");
        }

        var productVersion = FileVersionInfo
            .GetVersionInfo(msBuildAssemblyPath)
            .ProductVersion;
        var runtime = new MsBuildRuntimeIdentity(
            sdk.Version,
            string.IsNullOrWhiteSpace(productVersion)
                ? assemblyName.Version.ToString()
                : productVersion);

        lock (RegistrationGate)
        {
            var loadedMsBuildPath = _registrar.LoadedMsBuildPath();
            return ResolveRegistration(
                runtime,
                sdk.SdkPath,
                _registrar,
                loadedMsBuildPath,
                _registeredMsBuildPath,
                static path => _registeredMsBuildPath = path);
        }
    }

    internal static MsBuildAuthorityResult ResolveRegistration(
        MsBuildRuntimeIdentity runtime,
        string selectedMsBuildPath,
        IMsBuildRuntimeRegistrar registrar,
        string? loadedMsBuildPath,
        string? registeredMsBuildPath = null,
        Action<string>? recordRegistration = null)
    {
        var decision = RegistrationDecision(
            selectedMsBuildPath,
            registrar.IsRegistered,
            loadedMsBuildPath,
            registeredMsBuildPath);
        if (decision is MsBuildRegistrationDecision.Mismatch)
        {
            return Failure(
                ProjectEvaluationFailureReason.MsBuildIncompatible,
                "msbuild.registration_mismatch");
        }

        if (decision is MsBuildRegistrationDecision.UseRegistered)
        {
            recordRegistration?.Invoke(selectedMsBuildPath);
            return new MsBuildAuthorityResult(runtime, null);
        }

        try
        {
            registrar.RegisterMsBuildPath(selectedMsBuildPath);
            recordRegistration?.Invoke(selectedMsBuildPath);
            return new MsBuildAuthorityResult(runtime, null);
        }
        catch (InvalidOperationException)
        {
            return Failure(
                ProjectEvaluationFailureReason.MsBuildUnavailable,
                "msbuild.registration_failed");
        }
    }

    internal static MsBuildRegistrationDecision RegistrationDecision(
        string selectedMsBuildPath,
        bool isRegistered,
        string? loadedMsBuildPath,
        string? registeredMsBuildPath)
    {
        if (loadedMsBuildPath is not null)
        {
            return isRegistered
                   && PathsEqual(selectedMsBuildPath, loadedMsBuildPath)
                ? MsBuildRegistrationDecision.UseRegistered
                : MsBuildRegistrationDecision.Mismatch;
        }

        if (registeredMsBuildPath is not null)
        {
            return isRegistered
                   && PathsEqual(selectedMsBuildPath, registeredMsBuildPath)
                ? MsBuildRegistrationDecision.UseRegistered
                : MsBuildRegistrationDecision.Mismatch;
        }

        return isRegistered
            ? MsBuildRegistrationDecision.Mismatch
            : MsBuildRegistrationDecision.Register;
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);

    private static MsBuildAuthorityResult Failure(
        ProjectEvaluationFailureReason reason,
        string code) =>
        new(
            null,
            new ProjectEvaluationFailure(reason, code));
}
