using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetAxi.Structural.Tests;

public sealed class InvocationSyntaxQueryTests
{
    [Fact]
    public void Query_matches_supported_shapes_by_exact_terminal_value_text()
    {
        var root = CSharpSyntaxTree.ParseText(
                """
                class C
                {
                    void M(dynamic service)
                    {
                        Target();
                        service.Target();
                        service?.Target();
                        Target<int>();
                        @Target();
                        target();
                        Other();
                        var callback = Target;
                    }
                }
                """)
            .GetCompilationUnitRoot();

        var matches = new InvocationSyntaxQuery("Target")
            .FindCandidates(root)
            .Cast<InvocationExpressionSyntax>()
            .Select(invocation => invocation.ToString())
            .ToArray();

        Assert.Equal(
            ["Target()", "service.Target()", ".Target()", "Target<int>()", "@Target()"],
            matches);
    }

    [Fact]
    public void Query_keeps_a_recoverable_malformed_invocation_candidate()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { void M() { Target(");
        var root = tree.GetCompilationUnitRoot();

        var match = Assert.Single(
            new InvocationSyntaxQuery("Target").FindCandidates(root));

        Assert.IsType<InvocationExpressionSyntax>(match);
        Assert.NotEmpty(tree.GetDiagnostics());
    }

    [Fact]
    public void Query_identity_is_parameter_sensitive_and_input_is_validated()
    {
        var first = new InvocationSyntaxQuery("First");
        var second = new InvocationSyntaxQuery("Second");

        Assert.Equal("invocation", first.Kind);
        Assert.NotEqual(first.Identity, second.Identity);
        Assert.Throws<ArgumentException>(() => new InvocationSyntaxQuery(" "));
    }

    [Fact]
    public void Query_honors_pre_cancelled_discovery_even_without_invocations()
    {
        var root = CSharpSyntaxTree.ParseText("class C { }")
            .GetCompilationUnitRoot();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new InvocationSyntaxQuery("Target")
                .FindCandidates(root, cancellation.Token)
                .ToArray());
    }
}
