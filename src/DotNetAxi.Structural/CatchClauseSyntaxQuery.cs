using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetAxi.Structural;

/// <summary>Finds catch clauses by optional terminal type name and empty-body intent.</summary>
public sealed class CatchClauseSyntaxQuery : ISemanticallyVerifiableSyntaxQuery
{
    public CatchClauseSyntaxQuery(string? type = null, bool emptyOnly = false)
    {
        if (type is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(type);
            if (type.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A catch type cannot contain a null character.",
                    nameof(type));
            }
        }

        Type = type;
        EmptyOnly = emptyOnly;
        var typeIdentity = type is null
            ? "any"
            : "name/" + Convert.ToHexStringLower(Encoding.UTF8.GetBytes(type));
        Identity = $"catch/v1/type/{typeIdentity}/empty/{(emptyOnly ? "true" : "false")}";
    }

    public string? Type { get; }

    public bool EmptyOnly { get; }

    public string Kind => "catch";

    public string Identity { get; }

    public SemanticSyntaxVerifier Verifier => new(
        SemanticSyntaxVerifierKind.CatchClause,
        Type);

    public IEnumerable<SyntaxNode> FindCandidates(
        CompilationUnitSyntax root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is CatchClauseSyntax clause
                && MatchesType(clause)
                && (!EmptyOnly || clause.Block.Statements.Count == 0))
            {
                yield return clause;
            }
        }
    }

    private bool MatchesType(CatchClauseSyntax clause)
    {
        if (Type is null)
        {
            return true;
        }

        return clause.Declaration is { Type: { } declarationType }
            && TerminalTypeName(declarationType) is { } terminalName
            && terminalName.Equals(Type, StringComparison.Ordinal);
    }

    private static string? TerminalTypeName(TypeSyntax type) =>
        type switch
        {
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            PredefinedTypeSyntax predefined => predefined.Keyword.ValueText,
            NullableTypeSyntax nullable => TerminalTypeName(nullable.ElementType),
            ArrayTypeSyntax array => TerminalTypeName(array.ElementType),
            PointerTypeSyntax pointer => TerminalTypeName(pointer.ElementType),
            _ => null,
        };
}
