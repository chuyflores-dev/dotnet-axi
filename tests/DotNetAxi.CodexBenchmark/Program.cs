using DotNetAxi.Testing;

return await CodexDiscoveryBenchmarkProgram.RunAsync(args);

internal static class CodexDiscoveryBenchmarkProgram
{
    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "prepare" => await PrepareAsync(args[1..]),
                "run" => await RunSeriesAsync(args[1..]),
                "validate" => await ValidateAsync(args[1..]),
                "hash-file" => await HashFileAsync(args[1..]),
                "hash-directory" => await HashDirectoryAsync(args[1..]),
                _ => Usage(),
            };
        }
        catch (Exception exception)
            when (exception is AgentBenchmarkException
                  or ArgumentException
                  or IOException
                  or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> PrepareAsync(string[] args)
    {
        if (!TryReadTwoOptions(
                args,
                "--request",
                "--output",
                out var requestPath,
                out var outputPath))
        {
            return Usage();
        }

        var context = await CodexDiscoveryBenchmarkPreparation.PrepareAsync(
            requestPath);
        await CodexDiscoveryBenchmarkPreparation.WriteCreateNewAsync(
            outputPath,
            context.Preparation);
        PrintPreparation(context.Preparation);
        return 0;
    }

    private static async Task<int> RunSeriesAsync(string[] args)
    {
        if (!TryReadThreeOptions(
                args,
                "--request",
                "--preparation",
                "--evidence",
                out var requestPath,
                out var preparationPath,
                out var evidenceDirectory))
        {
            return Usage();
        }

        var context = await CodexDiscoveryBenchmarkPreparation
            .ValidatePreparationAsync(
            requestPath,
            preparationPath);
        var store = await CodexDiscoveryEvidenceStore.CreateAsync(
            evidenceDirectory,
            context);
        try
        {
            _ = await new AgentBenchmarkRunner().RunRetainedAsync(
                context.Corpus,
                context.Configuration,
                context.Adapter,
                store);
            var summary = await store.FinalizeAsync(
                completed: true,
                failure: null);
            PrintSummary(summary);
            return IsReleaseBlocking(summary.Comparison)
                ? 1
                : 0;
        }
        catch (Exception exception)
            when (exception is AgentBenchmarkException
                  or IOException
                  or UnauthorizedAccessException)
        {
            var failure = $"{exception.GetType().Name}: {exception.Message}";
            var summary = await store.FinalizeAsync(
                completed: false,
                failure);
            PrintSummary(summary);
            Console.Error.WriteLine(failure);
            return 1;
        }
    }

    private static async Task<int> ValidateAsync(string[] args)
    {
        if (!TryReadTwoOptions(
                args,
                "--request",
                "--evidence",
                out var requestPath,
                out var evidenceDirectory))
        {
            return Usage();
        }

        var summary =
            await CodexDiscoveryEvidenceValidator.ValidateDirectoryAsync(
                requestPath,
                evidenceDirectory);
        PrintSummary(summary);
        return IsReleaseBlocking(summary.Comparison)
            ? 1
            : 0;
    }

    private static async Task<int> HashFileAsync(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]))
        {
            return Usage();
        }

        Console.WriteLine(
            await CodexDiscoveryBenchmarkPreparation.HashFileAsync(args[0]));
        return 0;
    }

    private static async Task<int> HashDirectoryAsync(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]))
        {
            return Usage();
        }

        Console.WriteLine(
            await CodexDiscoveryBenchmarkPreparation.HashDirectoryAsync(
                args[0]));
        return 0;
    }

    private static void PrintPreparation(
        CodexDiscoverySeriesPreparation preparation)
    {
        Console.WriteLine($"prepared_runs: {preparation.Schedule.Count}");
        Console.WriteLine(
            $"agent_timeout_budget_seconds: {preparation.UsageBoundary.AgentTimeoutBudgetSeconds}");
        Console.WriteLine(
            $"finalization_budget_seconds: {preparation.UsageBoundary.FinalizationBudgetSeconds}");
        Console.WriteLine(
            $"provider_token_limit: {(preparation.UsageBoundary.ProviderTokenLimit?.ToString() ?? "none")}");
        Console.WriteLine(
            $"authentication_method: {preparation.Pins.AuthenticationMethod}");
        Console.WriteLine("candidate_execution_preflight: passed");
        Console.WriteLine(
            $"isolation_preflight: {preparation.Isolation.Protocol} baseline=passed candidate=passed");
        Console.WriteLine(
            $"request_hash: {preparation.RequestHash}");
    }

    private static void PrintSummary(CodexDiscoverySeriesSummary summary)
    {
        Console.WriteLine($"evidence_status: {summary.EvidenceStatus}");
        Console.WriteLine($"comparison: {summary.Comparison}");
        Console.WriteLine(
            $"retained_runs: {summary.RetainedRunCount}/{summary.ExpectedRunCount}");
        Console.WriteLine(
            $"baseline_success_percent: {summary.Baseline.SuccessRatePercent}");
        Console.WriteLine(
            $"candidate_success_percent: {summary.Candidate.SuccessRatePercent}");
        Console.WriteLine(
            $"median_token_change_percent: {summary.Thresholds.MedianTokenChangePercent?.ToString() ?? "unavailable"}");
        Console.WriteLine(
            $"median_duration_change_percent: {summary.Thresholds.MedianDurationChangePercent?.ToString() ?? "unavailable"}");
        Console.WriteLine(
            $"improvement_claim_supported: {summary.Thresholds.ImprovementClaimSupported.ToString().ToLowerInvariant()}");
        Console.WriteLine(
            $"activated_routes: {summary.RouteActivations.Count(static route => route.SuccessfulActivatedRunCount > 0)}/{summary.RouteActivations.Count}");
    }

    private static bool IsReleaseBlocking(string comparison) =>
        comparison is "regression"
            or "incomparable"
            or "zero-activation"
            or "activation-gap";

    private static bool TryReadTwoOptions(
        string[] args,
        string firstName,
        string secondName,
        out string first,
        out string second)
    {
        first = string.Empty;
        second = string.Empty;
        if (args.Length != 4)
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!values.TryAdd(args[index], args[index + 1]))
            {
                return false;
            }
        }

        return values.TryGetValue(firstName, out first!)
               && values.TryGetValue(secondName, out second!)
               && values.Count == 2
               && Path.IsPathFullyQualified(first)
               && Path.IsPathFullyQualified(second);
    }

    private static bool TryReadThreeOptions(
        string[] args,
        string firstName,
        string secondName,
        string thirdName,
        out string first,
        out string second,
        out string third)
    {
        first = string.Empty;
        second = string.Empty;
        third = string.Empty;
        if (args.Length != 6)
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!values.TryAdd(args[index], args[index + 1]))
            {
                return false;
            }
        }

        return values.TryGetValue(firstName, out first!)
               && values.TryGetValue(secondName, out second!)
               && values.TryGetValue(thirdName, out third!)
               && values.Count == 3
               && Path.IsPathFullyQualified(first)
               && Path.IsPathFullyQualified(second)
               && Path.IsPathFullyQualified(third);
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            "Usage:\n"
            + "  prepare --request /absolute/request.json --output /absolute/new-preparation.json\n"
            + "  run --request /absolute/request.json --preparation /absolute/preparation.json --evidence /absolute/new-evidence-directory\n"
            + "  validate --request /absolute/request.json --evidence /absolute/evidence-directory\n"
            + "  hash-file /absolute/file\n"
            + "  hash-directory /absolute/directory");
        return 64;
    }
}
