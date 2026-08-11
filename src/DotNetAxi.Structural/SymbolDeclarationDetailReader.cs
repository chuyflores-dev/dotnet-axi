using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotNetAxi.Structural;

public sealed record SymbolDeclarationDetail(
    string? ContainingType,
    string Documentation,
    string Body,
    SymbolRelationshipSummary Relationships);

public sealed record SymbolRelationshipSummary(
    int AttributeCount,
    int ParameterCount,
    int TypeParameterCount,
    int MemberCount,
    int BaseTypeCount,
    int OverloadCount);

/// <summary>
/// Reads bounded-command inputs for one declaration without loading a
/// compilation. The caller remains responsible for bounding returned text.
/// </summary>
public sealed class SymbolDeclarationDetailReader
{
    private static readonly Encoding DefaultEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public ValueTask<SymbolDeclarationDetail> ReadAsync(
        SymbolDeclarationMatch match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (match.SourceBytes.Length == 0)
        {
            throw new ArgumentException(
                "The symbol match does not carry source resolution metadata.",
                nameof(match));
        }

        var source = SourceText.From(
            match.SourceBytes,
            match.SourceBytes.Length,
            DefaultEncoding,
            SourceHashAlgorithm.Sha256,
            throwIfBinaryDetected: true,
            canBeEmbedded: false);
        var tree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default,
            match.Range.Start.Path,
            cancellationToken);
        var root = tree.GetRoot(cancellationToken);
        var span = new TextSpan(match.SourceSpanStart, match.SourceSpanLength);
        var node = root.FindNode(span, getInnermostNodeForTie: true);
        if (node.Span != span)
        {
            throw new InvalidOperationException(
                "The resolved declaration no longer matches its source span.");
        }

        return ValueTask.FromResult(new SymbolDeclarationDetail(
            ContainingType(node),
            Documentation(node),
            Body(node),
            Relationships(node)));
    }

    private static string? ContainingType(SyntaxNode node)
    {
        var containers = node.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Reverse()
            .Select(static declaration => declaration.Identifier.ValueText)
            .ToArray();
        if (containers.Length == 0)
        {
            return null;
        }

        var namespaceName = string.Join(
            '.',
            node.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(static declaration => declaration.Name.ToString()));
        return namespaceName.Length == 0
            ? string.Join('.', containers)
            : namespaceName + "." + string.Join('.', containers);
    }

    private static string Documentation(SyntaxNode node) =>
        string.Join(
                "\n",
                DocumentationAnchor(node)
                    .GetLeadingTrivia()
                    .Where(static trivia =>
                        trivia.GetStructure() is DocumentationCommentTriviaSyntax)
                    .Select(static trivia => trivia.ToFullString().Trim()))
            .Trim();

    private static SyntaxNode DocumentationAnchor(SyntaxNode node) =>
        node is VariableDeclaratorSyntax
            ? node.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault()
                ?? node
            : node;

    private static string Body(SyntaxNode node) =>
        node switch
        {
            BaseMethodDeclarationSyntax declaration => Text(
                declaration.Body,
                declaration.ExpressionBody),
            PropertyDeclarationSyntax declaration => Text(
                declaration.AccessorList,
                declaration.ExpressionBody),
            IndexerDeclarationSyntax declaration => Text(
                declaration.AccessorList,
                declaration.ExpressionBody),
            EventDeclarationSyntax declaration =>
                declaration.AccessorList?.ToFullString().Trim() ?? string.Empty,
            TypeDeclarationSyntax declaration => string.Join(
                    "\n",
                    declaration.Members.Select(static member =>
                        member.ToFullString().Trim()))
                .Trim(),
            EnumDeclarationSyntax declaration => string.Join(
                    "\n",
                    declaration.Members.Select(static member =>
                        member.ToFullString().Trim()))
                .Trim(),
            BaseNamespaceDeclarationSyntax declaration => string.Join(
                    "\n",
                    declaration.Members.Select(static member =>
                        member.ToFullString().Trim()))
                .Trim(),
            VariableDeclaratorSyntax declaration =>
                declaration.Initializer?.ToFullString().Trim() ?? string.Empty,
            EnumMemberDeclarationSyntax declaration =>
                declaration.EqualsValue?.ToFullString().Trim() ?? string.Empty,
            _ => string.Empty,
        };

    private static string Text(
        SyntaxNode? block,
        ArrowExpressionClauseSyntax? expressionBody) =>
        (block ?? expressionBody)?.ToFullString().Trim() ?? string.Empty;

    private static SymbolRelationshipSummary Relationships(SyntaxNode node)
    {
        var member = DocumentationAnchor(node) as MemberDeclarationSyntax;
        return new SymbolRelationshipSummary(
            member?.AttributeLists.Sum(static list => list.Attributes.Count) ?? 0,
            ParameterCount(node),
            TypeParameterCount(node),
            MemberCount(node),
            BaseTypeCount(node),
            OverloadCount(node));
    }

    private static int ParameterCount(SyntaxNode node) =>
        node switch
        {
            BaseMethodDeclarationSyntax declaration =>
                declaration.ParameterList.Parameters.Count,
            DelegateDeclarationSyntax declaration =>
                declaration.ParameterList.Parameters.Count,
            IndexerDeclarationSyntax declaration =>
                declaration.ParameterList.Parameters.Count,
            ClassDeclarationSyntax declaration =>
                declaration.ParameterList?.Parameters.Count ?? 0,
            StructDeclarationSyntax declaration =>
                declaration.ParameterList?.Parameters.Count ?? 0,
            RecordDeclarationSyntax declaration =>
                declaration.ParameterList?.Parameters.Count ?? 0,
            _ => 0,
        };

    private static int TypeParameterCount(SyntaxNode node) =>
        node switch
        {
            TypeDeclarationSyntax declaration =>
                declaration.TypeParameterList?.Parameters.Count ?? 0,
            MethodDeclarationSyntax declaration =>
                declaration.TypeParameterList?.Parameters.Count ?? 0,
            DelegateDeclarationSyntax declaration =>
                declaration.TypeParameterList?.Parameters.Count ?? 0,
            _ => 0,
        };

    private static int MemberCount(SyntaxNode node) =>
        node switch
        {
            TypeDeclarationSyntax declaration => declaration.Members.Count,
            EnumDeclarationSyntax declaration => declaration.Members.Count,
            BaseNamespaceDeclarationSyntax declaration => declaration.Members.Count,
            _ => 0,
        };

    private static int BaseTypeCount(SyntaxNode node) =>
        node is BaseTypeDeclarationSyntax declaration
            ? declaration.BaseList?.Types.Count ?? 0
            : 0;

    private static int OverloadCount(SyntaxNode node) =>
        node switch
        {
            MethodDeclarationSyntax declaration => SiblingCount(
                declaration,
                sibling => sibling is MethodDeclarationSyntax candidate
                    && candidate.Identifier.ValueText.Equals(
                        declaration.Identifier.ValueText,
                        StringComparison.Ordinal)),
            ConstructorDeclarationSyntax declaration => SiblingCount(
                declaration,
                sibling => sibling is ConstructorDeclarationSyntax),
            IndexerDeclarationSyntax declaration => SiblingCount(
                declaration,
                sibling => sibling is IndexerDeclarationSyntax),
            OperatorDeclarationSyntax declaration => SiblingCount(
                declaration,
                sibling => sibling is OperatorDeclarationSyntax candidate
                    && candidate.OperatorToken.ValueText.Equals(
                        declaration.OperatorToken.ValueText,
                        StringComparison.Ordinal)),
            ConversionOperatorDeclarationSyntax declaration => SiblingCount(
                declaration,
                sibling => sibling is ConversionOperatorDeclarationSyntax candidate
                    && candidate.ImplicitOrExplicitKeyword.ValueText.Equals(
                        declaration.ImplicitOrExplicitKeyword.ValueText,
                        StringComparison.Ordinal)
                    && candidate.Type.WithoutTrivia().ToString().Equals(
                        declaration.Type.WithoutTrivia().ToString(),
                        StringComparison.Ordinal)),
            _ => 0,
        };

    private static int SiblingCount(
        SyntaxNode declaration,
        Func<SyntaxNode, bool> predicate) =>
        declaration.Parent?.ChildNodes().Count(predicate) ?? 1;
}
