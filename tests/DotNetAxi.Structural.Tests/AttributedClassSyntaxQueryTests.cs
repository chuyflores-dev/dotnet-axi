using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetAxi.Structural.Tests;

public sealed class AttributedClassSyntaxQueryTests
{
    [Fact]
    public void Query_matches_supported_attribute_shapes_and_class_kinds_once()
    {
        var root = CSharpSyntaxTree.ParseText(
                """
                [assembly: Authorize]

                [Authorize]
                class Simple { }

                [Security.Authorize]
                static class Qualified { }

                [AuthorizeAttribute, Obsolete]
                partial class Suffixed { }

                [global::Security.AuthorizeAttribute]
                class AliasQualified { }

                [type: Authorize]
                class Targeted { }

                [Authorize, AuthorizeAttribute]
                class Multiple { }

                [@Authorize]
                class Escaped { }

                class Outer
                {
                    [Authorize]
                    private class Nested { }
                }

                [authorize]
                class WrongCase { }

                [Other]
                class Other { }

                [Authorize]
                record RecordCandidate;

                [Authorize]
                struct StructCandidate { }

                [Authorize]
                interface InterfaceCandidate { }
                """)
            .GetCompilationUnitRoot();

        var matches = new AttributedClassSyntaxQuery("Authorize")
            .FindCandidates(root)
            .Cast<ClassDeclarationSyntax>()
            .Select(declaration => declaration.Identifier.ValueText)
            .ToArray();

        Assert.Equal(
            [
                "Simple",
                "Qualified",
                "Suffixed",
                "AliasQualified",
                "Targeted",
                "Multiple",
                "Escaped",
                "Nested",
            ],
            matches);
    }

    [Fact]
    public void Query_normalizes_the_optional_attribute_suffix_on_both_sides()
    {
        var root = CSharpSyntaxTree.ParseText(
                """
                [Authorize]
                class Short { }

                [AuthorizeAttribute]
                class Suffixed { }

                [Attribute]
                class BaseAttributeName { }
                """)
            .GetCompilationUnitRoot();

        var shortRequest = Names(new AttributedClassSyntaxQuery("Authorize"), root);
        var suffixedRequest = Names(
            new AttributedClassSyntaxQuery("AuthorizeAttribute"),
            root);
        var baseAttributeRequest = Names(
            new AttributedClassSyntaxQuery("Attribute"),
            root);

        Assert.Equal(["Short", "Suffixed"], shortRequest);
        Assert.Equal(shortRequest, suffixedRequest);
        Assert.Equal(["BaseAttributeName"], baseAttributeRequest);
    }

    [Fact]
    public void Query_keeps_a_recoverable_malformed_attributed_class_candidate()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            [Authorize
            class Broken { }
            """);
        var root = tree.GetCompilationUnitRoot();

        var match = Assert.Single(
            new AttributedClassSyntaxQuery("Authorize").FindCandidates(root));

        Assert.Equal("Broken", Assert.IsType<ClassDeclarationSyntax>(match).Identifier.ValueText);
        Assert.NotEmpty(tree.GetDiagnostics());
    }

    [Fact]
    public void Query_identity_is_parameter_sensitive_and_input_is_validated()
    {
        var first = new AttributedClassSyntaxQuery("First");
        var firstWithSuffix = new AttributedClassSyntaxQuery("FirstAttribute");
        var second = new AttributedClassSyntaxQuery("Second");

        Assert.Equal("class", first.Kind);
        Assert.Equal(first.Identity, firstWithSuffix.Identity);
        Assert.NotEqual(first.Identity, second.Identity);
        Assert.Throws<ArgumentException>(() => new AttributedClassSyntaxQuery(" "));
    }

    [Fact]
    public void Query_honors_pre_cancelled_discovery_even_without_classes()
    {
        var root = CSharpSyntaxTree.ParseText("struct S { }")
            .GetCompilationUnitRoot();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new AttributedClassSyntaxQuery("Authorize")
                .FindCandidates(root, cancellation.Token)
                .ToArray());
    }

    private static string[] Names(
        AttributedClassSyntaxQuery query,
        CompilationUnitSyntax root) =>
        query.FindCandidates(root)
            .Cast<ClassDeclarationSyntax>()
            .Select(declaration => declaration.Identifier.ValueText)
            .ToArray();
}
