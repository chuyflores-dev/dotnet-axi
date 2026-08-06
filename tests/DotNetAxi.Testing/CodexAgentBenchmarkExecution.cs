using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DotNetAxi.Testing;

internal sealed class CodexAgentBenchmarkExecution
    : IAgentBenchmarkExecution
{
    private readonly Process _process;
    private readonly CodexAgentBenchmarkEventNormalizer _normalizer;
    private readonly Task _standardOutput;
    private readonly Task _standardError;
    private readonly object _lifecycleGate = new();
    private Task? _stopTask;
    private Task? _disposeTask;

    public CodexAgentBenchmarkExecution(
        Process process,
        AgentBenchmarkAdapterInput input)
    {
        _process = process;
        _normalizer = new CodexAgentBenchmarkEventNormalizer(input);
        _normalizer.AddAdapterEvent(
            "adapter.process.started",
            JsonSerializer.Serialize(new
            {
                processId = process.Id,
                workspacePath = Path.GetFullPath(input.WorkspacePath),
            }));
        _standardOutput = ReadStandardOutputAsync();
        _standardError = ReadStandardErrorAsync();
        Completion = CompleteAsync();
    }

    public Task<AgentBenchmarkAdapterResult> Completion { get; }

    public AgentBenchmarkProgressSnapshot GetProgressSnapshot() =>
        _normalizer.GetProgressSnapshot();

    public ValueTask StopAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            _stopTask ??= StopCoreAsync(cancellationToken);
            return new ValueTask(_stopTask);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task<AgentBenchmarkAdapterResult> CompleteAsync()
    {
        await _process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(_standardOutput, _standardError)
            .ConfigureAwait(false);
        var exitCode = _process.ExitCode;
        _normalizer.AddAdapterEvent(
            "adapter.process.exited",
            JsonSerializer.Serialize(new
            {
                processId = _process.Id,
                exitCode,
            }));
        return _normalizer.CreateResult(exitCode);
    }

    private async Task ReadStandardOutputAsync()
    {
        var buffer = new char[4096];
        var pending = new StringBuilder();
        while (true)
        {
            var read = await _process.StandardOutput.ReadAsync(buffer)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                if (character == '\n')
                {
                    var line = pending.ToString();
                    pending.Clear();
                    if (line.EndsWith('\r'))
                    {
                        line = line[..^1];
                    }

                    _normalizer.AddProviderLine(line);
                }
                else
                {
                    pending.Append(character);
                }
            }
        }

        if (pending.Length > 0)
        {
            _normalizer.AddTruncatedProviderLine(pending.ToString());
        }
    }

    private async Task ReadStandardErrorAsync()
    {
        while (await _process.StandardError.ReadLineAsync()
                   .ConfigureAwait(false) is { } line)
        {
            _normalizer.AddStandardError(line);
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The exact owned process exited between the liveness check
                // and the bounded stop request.
            }
        }

        await _process.WaitForExitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            if (_process.HasExited)
            {
                await Completion.ConfigureAwait(false);
            }
        }
        finally
        {
            _process.Dispose();
        }
    }
}
