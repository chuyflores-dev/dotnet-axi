using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotNetAxi.Testing.Tests;

public sealed partial class SymbolContextCorpusTests
{
    [Fact]
    public async Task Corpus_declares_the_complete_deterministic_0_5_0_slice()
    {
        var corpus = await AgentTaskCorpusLoader.LoadAsync(CorpusPath());

        Assert.Equal("symbol-context", corpus.Id);
        Assert.Equal("1.0.2", corpus.Version);
        Assert.Equal(10, corpus.Tasks.Count);
        Assert.Equal(
            [
                "test-symbol-explicit-scope",
                "symbol-owner-framework-variants",
                "fresh-symbol-identity-show",
                "stale-symbol-correction",
                "ambiguous-symbol-correction",
                "syntax-candidate-partial-verification",
                "bounded-symbol-show",
                "document-exact-line-span",
                "symbol-outline",
                "context-whole-section-truncation",
            ],
            corpus.Tasks.Select(static task => task.Id));
        Assert.All(
            corpus.Tasks,
            static task =>
            {
                Assert.Equal("0.5.0", task.Milestone);
                Assert.True(task.Applicability.Candidate);
                Assert.Equal("materialized-clean", task.Repository.State);
                Assert.Equal("disabled", task.Execution.Network);
                Assert.Equal("invariant", task.Execution.Locale);
                Assert.Equal("UTC", task.Execution.TimeZone);
                Assert.Equal("exact-fact-set", task.SuccessOracle.Kind);
                Assert.Equal("ordinal-sequence/v1", task.SuccessOracle.Normalizer);
                Assert.Null(task.SuccessOracle.ModelJudge);
                Assert.Contains(
                    "Replace every angle-bracket description with the corresponding evidence value.",
                    task.Prompt,
                    StringComparison.Ordinal);
                const string responseMarker =
                    "with each shown literal prefix followed by one space and no extra text:";
                Assert.Contains(
                    responseMarker,
                    task.Prompt,
                    StringComparison.Ordinal);
                var contract = task.Prompt[
                    (task.Prompt.IndexOf(
                        responseMarker,
                        StringComparison.Ordinal) + responseMarker.Length)..];
                var offset = 0;
                foreach (var expectedFact in task.SuccessOracle.ExpectedFacts)
                {
                    var prefix = expectedFact[..(expectedFact.IndexOf(':') + 1)];
                    var marker = $"`{prefix} ";
                    var markerIndex = contract.IndexOf(
                        marker,
                        offset,
                        StringComparison.Ordinal);
                    Assert.True(
                        markerIndex >= offset,
                        $"Task '{task.Id}' does not declare grammar for '{prefix}'.");
                    offset = markerIndex + marker.Length;
                }
                Assert.Equal(
                    task.SuccessOracle.ExpectedFacts.Order(StringComparer.Ordinal),
                    task.SuccessOracle.ExpectedFacts);
                Assert.Equal(
                    ["claims-supported", "network-unused", "workspace-unchanged"],
                    task.SafetyOracle.Checks);
                Assert.Equal(
                    ["fixture-content-hash", "safety-oracle", "success-oracle"],
                    task.RequiredValidation);
            });
        Assert.Equal(
            [
                "test-symbol-explicit-scope",
                "symbol-owner-framework-variants",
                "syntax-candidate-partial-verification",
                "document-exact-line-span",
            ],
            corpus.Tasks
                .Where(static task => task.Applicability.Baseline)
                .Select(static task => task.Id));
        Assert.Equal(
            corpus.Tasks.Select(static task => task.Id),
            corpus.Tasks
                .Where(static task => task.Applicability.Candidate)
                .Select(static task => task.Id));
    }

    [Fact]
    public async Task Exact_fact_normalizer_preserves_order_and_duplicates()
    {
        var corpus = await AgentTaskCorpusLoader.LoadAsync(CorpusPath());
        var facts = corpus.Tasks[0].SuccessOracle.ExpectedFacts;
        var exact = string.Join('\n', facts);
        var reordered = string.Join('\n', facts.Reverse());
        var duplicated = string.Join('\n', [.. facts, facts[0]]);

        Assert.True(AgentBenchmarkFactSet.EqualsExpected(
            exact,
            facts,
            "ordinal-sequence/v1"));
        Assert.False(AgentBenchmarkFactSet.EqualsExpected(
            reordered,
            facts,
            "ordinal-sequence/v1"));
        Assert.False(AgentBenchmarkFactSet.EqualsExpected(
            duplicated,
            facts,
            "ordinal-sequence/v1"));
        Assert.True(AgentBenchmarkFactSet.EqualsExpected(
            duplicated,
            facts,
            "ordinal-lines/v1"));
    }

    [Theory]
    [InlineData("missing-grammar")]
    [InlineData("malformed-code-span")]
    [InlineData("extra-declaration")]
    [InlineData("leaked-answer")]
    [InlineData("vague-grammar")]
    [InlineData("missing-order-directive")]
    public async Task Preparation_contract_rejects_under_specified_or_leaking_prompts(
        string mutation)
    {
        var corpus = await AgentTaskCorpusLoader.LoadAsync(CorpusPath());
        var task = corpus.Tasks[0];
        var prompt = mutation switch
        {
            "missing-grammar" => task.Prompt.Replace(
                "<repo-relative-path>:<1-based-line>",
                "value",
                StringComparison.Ordinal),
            "malformed-code-span" => task.Prompt.Replace(
                "`declaration: <repo-relative-path>:<1-based-line>`",
                "`declaration: <repo-relative-path>:<1-based-line>",
                StringComparison.Ordinal),
            "extra-declaration" => task.Prompt[..^1]
                + ", `extra: <value>`.",
            "leaked-answer" => task.Prompt.Replace(
                "declaration: <repo-relative-path>:<1-based-line>",
                task.SuccessOracle.ExpectedFacts[0],
                StringComparison.Ordinal),
            "vague-grammar" => task.Prompt.Replace(
                "declaration: <repo-relative-path>:<1-based-line>",
                "declaration: <value>",
                StringComparison.Ordinal),
            "missing-order-directive" => corpus.Tasks[1].Prompt.Replace(
                "Sort repeated framework values using ordinal string order. ",
                string.Empty,
                StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        var mutatedTask = mutation == "missing-order-directive"
            ? corpus.Tasks[1] with { Prompt = prompt }
            : task with { Prompt = prompt };

        Assert.False(
            CodexDiscoveryBenchmarkPreparation.HasExactFactResponseContract(
                mutatedTask));
    }

    [Fact]
    public async Task Fixed_fixture_proves_success_recovery_and_boundary_oracles()
    {
        var corpus = await AgentTaskCorpusLoader.LoadAsync(CorpusPath());
        var factory = new RepositoryFixtureFactory();
        await using var fixture = await factory.CreateAsync(FixtureManifestPath());

        var testScope = await RunAsync(
            fixture.WorkspacePath,
            "search", "symbol", "SymbolContext.Tests.ScopeProbe",
            "--solution", "Workspace.slnx",
            "--include-tests",
            "--fields", "id,signature,owning_projects,variant_count,variants",
            "--full");
        AssertSuccess(testScope);
        Assert.Contains("file: tests/Core.Tests/ScopeProbe.cs", testScope.Output);
        Assert.Contains("line: 3", testScope.Output);
        Assert.Contains("tests/Core.Tests/Core.Tests.csproj", testScope.Output);
        AssertTaskFacts(
            corpus,
            "test-symbol-explicit-scope",
            [
                $"declaration: {Scalar(testScope.Output, "file")}:{Scalar(testScope.Output, "line")}",
                $"owner: {InlineList(testScope.Output, "owning_projects").Single()}",
                $"signature: {Scalar(testScope.Output, "signature")}",
            ]);

        var variants = await RunAsync(
            fixture.WorkspacePath,
            "search", "symbol", "SymbolContext.Product.LedgerService",
            "--project", "src/Core/Core.csproj",
            "--fields", "id,signature,owning_projects,variant_count,variants",
            "--full");
        AssertSuccess(variants);
        Assert.Contains("file: src/Core/LedgerService.cs", variants.Output);
        Assert.Contains("line: 4", variants.Output);
        Assert.Contains("variant_count: 2", variants.Output);
        Assert.Contains("net8.0", variants.Output);
        Assert.Contains("net10.0", variants.Output);
        var frameworks = Regex.Matches(
                variants.Output,
                @"(?m)^\s+null,(?<framework>net[^,]+),unresolved,src/Core/Core\.csproj$")
            .Select(static match => match.Groups["framework"].Value)
            .ToArray();
        AssertTaskFacts(
            corpus,
            "symbol-owner-framework-variants",
            [
                $"declaration: {Scalar(variants.Output, "file")}:{Scalar(variants.Output, "line")}",
                .. frameworks.Select(static framework => $"framework: {framework}"),
                $"owner: {InlineList(variants.Output, "owning_projects").Single()}",
                $"variant-count: {Scalar(variants.Output, "variant_count")}",
            ]);

        var formatSearch = await RunAsync(
            fixture.WorkspacePath,
            "search", "symbol", "Format",
            "--project", "src/Core/Core.csproj",
            "--fields", "id,signature,owning_projects",
            "--full");
        AssertSuccess(formatSearch);
        var formatId = EntityId(formatSearch.Output);
        var formatShow = await RunAsync(
            fixture.WorkspacePath,
            "show", "symbol", formatId,
            "--project", "src/Core/Core.csproj");
        AssertSuccess(formatShow);
        Assert.Contains("signature: Format(string)", formatShow.Output);
        Assert.Contains("return $\\\"ledger:{value}\\\";", formatShow.Output);
        var bodyStatement = SectionScalar(formatShow.Output, "body", "preview")
            .Split('\n')
            .Select(static line => line.Trim())
            .Single(static line => line.StartsWith("return ", StringComparison.Ordinal));
        AssertTaskFacts(
            corpus,
            "fresh-symbol-identity-show",
            [
                $"body: {bodyStatement}",
                $"declaration: {SectionScalar(formatShow.Output, "location", "file")}:{SectionScalar(formatShow.Output, "location", "line")}",
                $"signature: {Scalar(formatShow.Output, "signature")}",
            ]);

        var staleId = await ReadOracleAsync(fixture, "stale-symbol-id.txt");
        var stale = await RunAsync(
            fixture.WorkspacePath,
            "show", "symbol", staleId,
            "--project", "src/Core/Core.csproj");
        Assert.Equal(1, stale.ExitCode);
        Assert.Contains("code: evidence.stale_id", stale.Output);
        Assert.Contains("Reconcile(string)", stale.Output);
        Assert.Contains("search symbol 'Reconcile'", stale.Output);
        var staleCode = SectionScalar(stale.Output, "error", "code");
        var staleQuery = Scalar(stale.Output, "query");
        var staleTarget = Regex.Match(
            staleQuery,
            @"search symbol '(?<target>[^']+)'").Groups["target"].Value;
        var replacement = Regex.Match(
            stale.Output,
            @"(?m)^  symbol/v2/[^,]+,method,Reconcile,(?<signature>[^,]+),")
            .Groups["signature"].Value;
        AssertTaskFacts(
            corpus,
            "stale-symbol-correction",
            [
                $"correction-target: {staleTarget}",
                $"replacement: {replacement}",
                $"status: {StatusFromErrorCode(staleCode)}",
            ]);

        var ambiguousId = await ReadOracleAsync(
            fixture,
            "ambiguous-symbol-id.txt");
        var ambiguous = await RunAsync(
            fixture.WorkspacePath,
            "show", "symbol", ambiguousId,
            "--project", "src/Core/Core.csproj");
        Assert.Equal(1, ambiguous.ExitCode);
        Assert.Contains("code: evidence.ambiguous_id", ambiguous.Output);
        Assert.Contains("candidate_count: 2", ambiguous.Output);
        var ambiguousCode = SectionScalar(ambiguous.Output, "error", "code");
        var selected = ambiguousCode == "evidence.ambiguous_id"
            && !Regex.IsMatch(ambiguous.Output, @"(?m)^resolved:")
                ? "none"
                : "unexpected";
        AssertTaskFacts(
            corpus,
            "ambiguous-symbol-correction",
            [
                $"candidate-count: {Scalar(ambiguous.Output, "candidate_count")}",
                $"selected: {selected}",
                $"status: {StatusFromErrorCode(ambiguousCode)}",
            ]);

        var partial = await RunAsync(
            fixture.WorkspacePath,
            "search", "syntax", "invocation",
            "--name", "MissingAudit",
            "--path", "loose/UnownedCandidate.cs",
            "--verify", "--full");
        AssertSuccess(partial);
        Assert.Contains("status: partial", partial.Output);
        Assert.Contains("coverage: partial", partial.Output);
        Assert.Contains("ownership.not_found", partial.Output);
        Assert.Contains("unresolved: 1", partial.Output);
        AssertTaskFacts(
            corpus,
            "syntax-candidate-partial-verification",
            [
                $"candidate: {Scalar(partial.Output, "file")}:{Scalar(partial.Output, "line")}",
                $"coverage: {Scalar(partial.Output, "coverage")}",
                $"reason: {Scalar(partial.Output, "partial_reason")}",
                $"status: {IndentedScalar(partial.Output, 4, "status")}",
            ]);

        var boundedShow = await RunAsync(
            fixture.WorkspacePath,
            "show", "symbol", EntityId(variants.Output),
            "--project", "src/Core/Core.csproj",
            "--max-chars", "24");
        AssertSuccess(boundedShow);
        Assert.Contains("truncated: true", boundedShow.Output);
        Assert.Contains("retrieval_command:", boundedShow.Output);
        AssertTaskFacts(
            corpus,
            "bounded-symbol-show",
            [
                $"declaration: {SectionScalar(boundedShow.Output, "location", "file")}:{SectionScalar(boundedShow.Output, "location", "line")}",
                $"retrieval: {Availability(boundedShow.Output, "retrieval_command")}",
                $"signature: {Scalar(boundedShow.Output, "signature")}",
                $"truncated: {AnyBooleanValue(boundedShow.Output, "truncated")}",
            ]);

        var document = await RunAsync(
            fixture.WorkspacePath,
            "show", "document", "docs/Runbook.txt",
            "--start-line", "5", "--end-line", "6", "--full");
        AssertSuccess(document);
        Assert.Contains("requested_span:\n  start_line: 5\n  end_line: 6", document.Output);
        Assert.Contains("actual_span:\n  start_line: 5\n  end_line: 6", document.Output);
        Assert.Contains(
            "use exact inclusive document line spans\\nline six is the bounded preview target",
            document.Output);
        Assert.DoesNotContain("line seven must remain", document.Output);
        var documentLines = Scalar(document.Output, "preview")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        AssertTaskFacts(
            corpus,
            "document-exact-line-span",
            [
                $"actual-span: {SectionScalar(document.Output, "actual_span", "start_line")}-{SectionScalar(document.Output, "actual_span", "end_line")}",
                $"line-5: {documentLines[0]}",
                $"line-6: {documentLines[1]}",
            ]);

        var outline = await RunAsync(
            fixture.WorkspacePath,
            "outline", EntityId(variants.Output),
            "--project", "src/Core/Core.csproj", "--full");
        AssertSuccess(outline);
        Assert.Contains("kind: class", outline.Output);
        Assert.Contains("name: LedgerService", outline.Output);
        Assert.Contains("signature: \"public string Name { get; }\"", outline.Output);
        Assert.Contains("signature: public string Format(string value);", outline.Output);
        var outlineFacts = OutlineItems(outline.Output)
            .Select((item, index) =>
                $"{index + 1}: depth {item.Depth} {item.Signature}")
            .ToArray();
        AssertTaskFacts(corpus, "symbol-outline", outlineFacts);

        var context = await RunAsync(
            fixture.WorkspacePath,
            "context", "symbol", EntityId(variants.Output),
            "--project", "src/Core/Core.csproj",
            "--include", "declaration,owner,document,outline",
            "--max-chars", "0");
        AssertSuccess(context);
        Assert.Contains("sections: []", context.Output);
        Assert.Contains("included_characters: 0", context.Output);
        Assert.Contains(
            "omitted_sections[4]: declaration,owner,document,outline",
            context.Output);
        Assert.Contains("truncated: true", context.Output);
        Assert.Contains("retrieval_command:", context.Output);
        AssertTaskFacts(
            corpus,
            "context-whole-section-truncation",
            [
                $"included-characters: {Scalar(context.Output, "included_characters")}",
                $"omitted: {string.Join(',', InlineList(context.Output, "omitted_sections"))}",
                $"retrieval: {Availability(context.Output, "retrieval_command")}",
                $"truncated: {AnyBooleanValue(context.Output, "truncated")}",
            ]);

        var unsupported = await RunAsync(
            fixture.WorkspacePath,
            "context", "symbol", EntityId(variants.Output),
            "--project", "src/Core/Core.csproj",
            "--include", "callers");
        Assert.Equal(2, unsupported.ExitCode);
        Assert.Contains(
            "code: capability.context_section_unavailable",
            unsupported.Output);
    }

    private static void AssertSuccess((int ExitCode, string Output) result) =>
        Assert.True(result.ExitCode == 0, result.Output);

    private static void AssertTaskFacts(
        AgentTaskCorpus corpus,
        string taskId,
        IReadOnlyList<string> actualFacts)
    {
        var expected = corpus.Tasks.Single(task => task.Id == taskId)
            .SuccessOracle.ExpectedFacts;
        Assert.Equal(
            actualFacts.Count,
            actualFacts.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            expected,
            actualFacts.Order(StringComparer.Ordinal));
    }

    private static string Scalar(
        string output,
        string field,
        int occurrence = 0)
    {
        var matches = Regex.Matches(
            output,
            $@"(?m)^[ \t]*(?:- )?{Regex.Escape(field)}: (?<value>.*)$");
        Assert.True(
            matches.Count > occurrence,
            $"Missing scalar '{field}' occurrence {occurrence}.{Environment.NewLine}{output}");
        return DecodeScalar(matches[occurrence].Groups["value"].Value);
    }

    private static string IndentedScalar(
        string output,
        int spaces,
        string field)
    {
        var match = Regex.Match(
            output,
            $@"(?m)^{new string(' ', spaces)}{Regex.Escape(field)}: (?<value>.*)$");
        Assert.True(match.Success, output);
        return DecodeScalar(match.Groups["value"].Value);
    }

    private static string SectionScalar(
        string output,
        string section,
        string field)
    {
        var match = Regex.Match(
            output,
            $@"(?m)^{Regex.Escape(section)}:\r?\n(?:  .*\r?\n)*?  {Regex.Escape(field)}: (?<value>.*)$");
        Assert.True(match.Success, output);
        return DecodeScalar(match.Groups["value"].Value);
    }

    private static IReadOnlyList<string> InlineList(
        string output,
        string field)
    {
        var match = Regex.Match(
            output,
            $@"(?m)^[ \t]*{Regex.Escape(field)}\[[0-9]+\]: (?<value>.*)$");
        Assert.True(match.Success, output);
        return match.Groups["value"].Value.Split(',');
    }

    private static string DecodeScalar(string value) =>
        value.StartsWith('"')
            ? JsonSerializer.Deserialize<string>(value)!
            : value;

    private static string StatusFromErrorCode(string code) => code switch
    {
        "evidence.stale_id" => "stale",
        "evidence.ambiguous_id" => "ambiguous",
        _ => throw new InvalidOperationException($"Unexpected error code '{code}'."),
    };

    private static string Availability(string output, string field) =>
        Regex.IsMatch(
            output,
            $@"(?m)^[ \t]*{Regex.Escape(field)}:")
                ? "available"
                : "unavailable";

    private static string AnyBooleanValue(string output, string field) =>
        Regex.IsMatch(
            output,
            $@"(?m)^[ \t]*{Regex.Escape(field)}: true$")
                ? "true"
                : "false";

    private static IReadOnlyList<(int Depth, string Signature)> OutlineItems(
        string output) =>
        Regex.Matches(
                output,
                @"(?ms)^  - id: .*?\n    kind: .*?\n    name: .*?\n    signature: (?<signature>.*?)\r?\n    attributes:.*?\r?\n    depth: (?<depth>[0-9]+)$")
            .Select(match =>
                (
                    int.Parse(
                        match.Groups["depth"].Value,
                        System.Globalization.CultureInfo.InvariantCulture),
                    DecodeScalar(match.Groups["signature"].Value)))
            .ToArray();

    private static string EntityId(string output)
    {
        var match = EntityIdRegex().Match(output);
        Assert.True(match.Success, output);
        return match.Value;
    }

    private static async Task<string> ReadOracleAsync(
        RepositoryFixture fixture,
        string fileName) =>
        (await File.ReadAllTextAsync(
            Path.Combine(fixture.WorkspacePath, "oracles", fileName))).Trim();

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(DotNetAxi.Cli.Program).Assembly.Location);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(string.IsNullOrEmpty(error), error);
        return (process.ExitCode, output);
    }

    private static string CorpusPath() => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures", "AgentTasks", "symbol-context", "corpus.json");

    private static string FixtureManifestPath() => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures", "AgentTasks", "symbol-context", "fixture.json");

    [GeneratedRegex(
        @"symbol/v2/[A-Za-z0-9_-]+/[a-f0-9]{64}/[a-f0-9]{64}",
        RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdRegex();
}
