using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.Build.Locator;

namespace DotNetAxi.Workspaces;

internal sealed record DotNetHostResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal interface IDotNetHostRunner
{
    DotNetHostResult Run(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

internal interface IDotNetHostProcess : IDisposable
{
    TextReader StandardOutput { get; }

    TextReader StandardError { get; }

    int ExitCode { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void TerminateTree();
}

internal interface IDotNetHostProcessFactory
{
    IDotNetHostProcess? Start(ProcessStartInfo startInfo);
}

internal sealed record SelectedDotNetSdk(
    string Version,
    string SdkPath);

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
    private readonly IDotNetHostRunner _runner;

    public DotNetSdkSelector()
        : this(new PathDotNetHostRunner())
    {
    }

    internal DotNetSdkSelector(IDotNetHostRunner runner)
    {
        _runner = runner;
    }

    public DotNetSdkSelection Select(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var version = _runner.Run(
            workspaceRoot,
            ["--version"],
            cancellationToken);
        if (version.ExitCode != 0)
        {
            return new DotNetSdkSelection(null, "sdk.selection_failed");
        }

        var selectedVersion = SingleLine(version.StandardOutput);
        if (selectedVersion is null)
        {
            return new DotNetSdkSelection(null, "sdk.selection_invalid");
        }

        var installed = _runner.Run(
            workspaceRoot,
            ["--list-sdks"],
            cancellationToken);
        if (installed.ExitCode != 0)
        {
            return new DotNetSdkSelection(null, "sdk.inventory_failed");
        }

        var matchingPaths = ParseInstalledSdks(installed.StandardOutput)
            .Where(sdk => sdk.Version.Equals(
                selectedVersion,
                StringComparison.Ordinal))
            .Select(static sdk => sdk.SdkPath)
            .Distinct(PathComparer())
            .ToArray();
        return matchingPaths.Length == 1
            ? new DotNetSdkSelection(
                new SelectedDotNetSdk(selectedVersion, matchingPaths[0]),
                null)
            : new DotNetSdkSelection(
                null,
                matchingPaths.Length == 0
                    ? "sdk.selected_instance_missing"
                    : "sdk.selected_instance_ambiguous");
    }

    internal static IReadOnlyList<SelectedDotNetSdk> ParseInstalledSdks(
        string output)
    {
        var sdks = new List<SelectedDotNetSdk>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            var separator = line.IndexOf(' ');
            var openBracket = line.LastIndexOf(" [", StringComparison.Ordinal);
            if (separator <= 0
                || openBracket != separator
                || !line.EndsWith(']'))
            {
                continue;
            }

            var version = line[..separator];
            var basePath = line[(openBracket + 2)..^1];
            if (version.Length == 0 || basePath.Length == 0)
            {
                continue;
            }

            try
            {
                sdks.Add(new SelectedDotNetSdk(
                    version,
                    Path.GetFullPath(Path.Combine(basePath, version))));
            }
            catch (Exception exception)
                when (exception is ArgumentException
                      or NotSupportedException
                      or PathTooLongException)
            {
                // Ignore malformed inventory lines from the host.
            }
        }

        return Array.AsReadOnly(sdks.ToArray());
    }

    private static string? SingleLine(string output)
    {
        var lines = output.Split('\n')
            .Select(static line => line.Trim().TrimEnd('\r'))
            .Where(static line => line.Length > 0)
            .ToArray();
        return lines.Length == 1 ? lines[0] : null;
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}

internal sealed class PathDotNetHostRunner : IDotNetHostRunner
{
    private const int DefaultOutputLimit = 64 * 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private readonly IDotNetHostProcessFactory _processFactory;
    private readonly Func<string?> _hostPathResolver;
    private readonly TimeSpan _timeout;
    private readonly int _outputLimit;

    public PathDotNetHostRunner()
        : this(
            new SystemDotNetHostProcessFactory(),
            ResolveHostPath,
            DefaultTimeout,
            DefaultOutputLimit)
    {
    }

    internal PathDotNetHostRunner(
        IDotNetHostProcessFactory processFactory,
        Func<string?> hostPathResolver,
        TimeSpan timeout,
        int outputLimit)
    {
        ArgumentNullException.ThrowIfNull(processFactory);
        ArgumentNullException.ThrowIfNull(hostPathResolver);
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputLimit);
        _processFactory = processFactory;
        _hostPathResolver = hostPathResolver;
        _timeout = timeout;
        _outputLimit = outputLimit;
    }

    public DotNetHostResult Run(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hostPath = _hostPathResolver();
        if (hostPath is null)
        {
            return new DotNetHostResult(-1, string.Empty, string.Empty);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = _processFactory.Start(startInfo);
            if (process is null)
            {
                return new DotNetHostResult(-1, string.Empty, string.Empty);
            }

            return RunStartedProcessAsync(process, cancellationToken)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                  or System.ComponentModel.Win32Exception
                  or IOException
                  or UnauthorizedAccessException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new DotNetHostResult(-1, string.Empty, string.Empty);
        }
    }

    internal static string? ResolveHostPath(
        string? pathValue,
        bool isWindows,
        Func<string, bool> fileExists,
        Func<string, bool> isExecutable)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(isExecutable);
        var executableName = isWindows
            ? "dotnet.exe"
            : "dotnet";
        if (pathValue is null)
        {
            return null;
        }

        foreach (var rawDirectory in pathValue.Split(Path.PathSeparator))
        {
            var directory = isWindows
                ? rawDirectory.Trim().Trim('"')
                : rawDirectory;
            if (isWindows && directory.Length == 0)
            {
                continue;
            }

            try
            {
                var candidate = Path.GetFullPath(
                    Path.Combine(directory, executableName));
                if (fileExists(candidate)
                    && (isWindows || isExecutable(candidate)))
                {
                    return candidate;
                }
            }
            catch (Exception exception)
                when (exception is ArgumentException
                      or NotSupportedException
                      or PathTooLongException
                      or IOException
                      or UnauthorizedAccessException)
            {
                // Continue through the selected PATH.
            }
        }

        return null;
    }

    private async Task<DotNetHostResult> RunStartedProcessAsync(
        IDotNetHostProcess process,
        CancellationToken cancellationToken)
    {
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        execution.CancelAfter(_timeout);
        var overflow = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var standardOutput = CaptureOutputAsync(
            process.StandardOutput,
            overflow,
            execution.Token);
        var standardError = CaptureOutputAsync(
            process.StandardError,
            overflow,
            execution.Token);
        var processExit = BoundOperation(
            process.WaitForExitAsync(execution.Token),
            execution.Token);
        var completion = CompleteProcessAsync(
            process,
            processExit,
            standardOutput,
            standardError);

        try
        {
            var first = await Task.WhenAny(completion, overflow.Task)
                .ConfigureAwait(false);
            if (ReferenceEquals(first, overflow.Task))
            {
                throw new HostOutputLimitExceededException();
            }

            var result = await completion.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested)
        {
            TerminateProcessTree(process);
            execution.Cancel();
            await ObserveAsync(completion).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return FailedResult();
        }
        catch (HostOutputLimitExceededException)
        {
            TerminateProcessTree(process);
            execution.Cancel();
            await ObserveAsync(completion).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return FailedResult();
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                  or IOException
                  or UnauthorizedAccessException)
        {
            TerminateProcessTree(process);
            execution.Cancel();
            await ObserveAsync(completion).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return FailedResult();
        }
    }

    private async Task<string> CaptureOutputAsync(
        TextReader reader,
        TaskCompletionSource overflow,
        CancellationToken cancellationToken)
    {
        var buffer = new char[_outputLimit >= 4096
            ? 4096
            : _outputLimit + 1];
        var output = new StringBuilder(Math.Min(4096, _outputLimit));
        while (true)
        {
            var remaining = _outputLimit - output.Length;
            var requested = remaining >= buffer.Length
                ? buffer.Length
                : remaining + 1;
            var count = await BoundOperation(
                    reader.ReadAsync(
                            buffer.AsMemory(0, requested),
                            cancellationToken)
                        .AsTask(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                return output.ToString();
            }

            if (count > remaining)
            {
                if (remaining > 0)
                {
                    output.Append(buffer, 0, remaining);
                }

                overflow.TrySetResult();
                throw new HostOutputLimitExceededException();
            }

            output.Append(buffer, 0, count);
        }
    }

    private static async Task<DotNetHostResult> CompleteProcessAsync(
        IDotNetHostProcess process,
        Task processExit,
        Task<string> standardOutput,
        Task<string> standardError)
    {
        await Task.WhenAll(processExit, standardOutput, standardError)
            .ConfigureAwait(false);
        return new DotNetHostResult(
            process.ExitCode,
            standardOutput.Result,
            standardError.Result);
    }

    private static Task BoundOperation(
        Task operation,
        CancellationToken cancellationToken)
    {
        ObserveEventually(operation);
        return operation.WaitAsync(cancellationToken);
    }

    private static Task<T> BoundOperation<T>(
        Task<T> operation,
        CancellationToken cancellationToken)
    {
        ObserveEventually(operation);
        return operation.WaitAsync(cancellationToken);
    }

    private static void ObserveEventually(Task operation)
    {
        _ = operation.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously
            | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The process failure is already translated, but every child task
            // must reach and have its terminal state observed before disposal.
        }
    }

    private static void TerminateProcessTree(IDotNetHostProcess process)
    {
        try
        {
            process.TerminateTree();
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                  or System.ComponentModel.Win32Exception
                  or NotSupportedException)
        {
            // A concurrently exited process needs no further termination.
        }
    }

    private static DotNetHostResult FailedResult() =>
        new(-1, string.Empty, string.Empty);

    private static string? ResolveHostPath() =>
        ResolveHostPath(
            Environment.GetEnvironmentVariable("PATH"),
            OperatingSystem.IsWindows(),
            File.Exists,
            IsExecutable);

    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        return PosixProcessAuthority.CanExecute(path);
    }

    private sealed class HostOutputLimitExceededException : IOException;
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

    public MsBuildRuntimeAuthority()
        : this(new DotNetSdkSelector(), new SystemMsBuildRuntimeRegistrar())
    {
    }

    internal MsBuildRuntimeAuthority(IDotNetSdkSelector selector)
        : this(selector, new SystemMsBuildRuntimeRegistrar())
    {
    }

    internal MsBuildRuntimeAuthority(
        IDotNetSdkSelector selector,
        IMsBuildRuntimeRegistrar registrar)
    {
        _selector = selector;
        _registrar = registrar;
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
        var msBuildAssemblyPath = Path.Combine(
            sdk.SdkPath,
            "Microsoft.Build.dll");
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
