using System.Text.Json;

namespace DotNetAxi.Testing;

public sealed class DeterministicFakeAgentBenchmarkAdapter
    : IAgentBenchmarkAdapter
{
    public AgentBenchmarkAdapterDescriptor Descriptor { get; } =
        new("deterministic-fake", "1.0.0");

    public ValueTask<IAgentBenchmarkExecution> StartAsync(
        AgentBenchmarkAdapterInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var answer = string.Join(
            "\n",
            input.Task.SuccessOracle.ExpectedFacts);
        var toolClass = input.Task.Execution.PermittedTools[0];
        var inputHash = AgentBenchmarkHash.Compute(
            $"{input.Task.Prompt}\n{input.Condition}\n{input.Repetition}");
        var inspectedFiles = input.Task.SuccessOracle.ExpectedFacts
            .Select(static fact =>
            {
                var separator = fact.LastIndexOf(':');
                return separator > 1
                    && int.TryParse(
                        fact.AsSpan(separator + 1),
                        out _)
                    ? fact[..separator]
                    : fact;
            })
            .Where(static value => value.EndsWith(
                ".cs",
                StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var rawEvents = new[]
        {
            RawEvent(
                0,
                "fake.start",
                JsonSerializer.Serialize(
                    new
                    {
                        input.RunId,
                        condition = input.Condition.ToString(),
                        input.Repetition,
                    })),
            RawEvent(
                1,
                "fake.result",
                JsonSerializer.Serialize(
                    new
                    {
                        answer,
                        inputTokens = input.Task.Prompt.Length + 11,
                        outputTokens = answer.Length + 7,
                    })),
        };
        var result = new AgentBenchmarkAdapterResult(
            "completed",
            answer,
            input.Task.Prompt.Length + 11,
            answer.Length + 7,
            Turns: 2,
            AgentBenchmarkSnapshots.List(
                new[]
                {
                    new AgentBenchmarkToolCall(
                        0,
                        toolClass,
                        "fake.lookup",
                        inputHash,
                        Succeeded: true),
                }),
            new AgentBenchmarkInspectedScope(
                AgentBenchmarkSnapshots.List(inspectedFiles),
                AgentBenchmarkSnapshots.List(
                    new[] { "src/Discovery/Discovery.csproj" })),
            ClaimsSupported: true,
            NetworkUsed: false,
            new AgentBenchmarkObservedConfiguration(
                input.Execution.AgentVersion,
                input.Execution.ModelId,
                input.Execution.ReasoningSetting,
                input.Execution.SettingsHash,
                input.Execution.PermissionProfile,
                input.Execution.NetworkPolicy,
                input.Task.Repository.ContentHash,
                input.PromptHash,
                input.InstructionsHash,
                input.ToolConfigurationHash),
            AgentBenchmarkSnapshots.List(rawEvents));
        return ValueTask.FromResult<IAgentBenchmarkExecution>(
            new CompletedFakeExecution(result, rawEvents));
    }

    private static AgentBenchmarkRawEvent RawEvent(
        int sequence,
        string kind,
        string payload) =>
        new(sequence, kind, payload, AgentBenchmarkHash.Compute(payload));

    private sealed class CompletedFakeExecution(
        AgentBenchmarkAdapterResult result,
        IReadOnlyList<AgentBenchmarkRawEvent> rawEvents)
        : IAgentBenchmarkExecution
    {
        public Task<AgentBenchmarkAdapterResult> Completion { get; } =
            Task.FromResult(result);

        public AgentBenchmarkProgressSnapshot GetProgressSnapshot() =>
            new(
                result.InputTokens,
                result.OutputTokens,
                result.Turns,
                result.ToolCalls,
                result.InspectedScope,
                rawEvents);

        public ValueTask StopAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
