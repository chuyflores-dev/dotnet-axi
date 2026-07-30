using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace FixtureTooling;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FixtureAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        "DAXI001",
        "Fixture class observed",
        "Fixture analyzer observed class '{0}'",
        "Fixture",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeClass,
            SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var identifier =
            ((Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax)
                context.Node).Identifier;
        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                identifier.GetLocation(),
                identifier.ValueText));
    }
}

[Generator]
public sealed class FixtureGenerator : IIncrementalGenerator
{
    public void Initialize(
        IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(
            static output => output.AddSource(
                "GeneratedMessage.g.cs",
                SourceText.From(
                    """
                    namespace FixtureApp;

                    public static class GeneratedMessage
                    {
                        public const string Value = "generated";
                    }
                    """,
                    Encoding.UTF8)));
    }
}
