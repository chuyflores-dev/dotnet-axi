using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetAxi.Structural;

/// <summary>
/// Finds explicit object and array creations by terminal syntactic type name,
/// while retaining target-typed object creations as unresolved candidates.
/// </summary>
public sealed class ObjectCreationSyntaxQuery : ISemanticallyVerifiableSyntaxQuery
{
    public ObjectCreationSyntaxQuery(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (type.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An object-creation type cannot contain a null character.",
                nameof(type));
        }

        Type = type;
        Identity = "object-creation/v1/type/"
            + Convert.ToHexStringLower(Encoding.UTF8.GetBytes(type));
    }

    public string Type { get; }

    public string Kind => "object-creation";

    public string Identity { get; }

    public SemanticSyntaxVerifier Verifier => new(
        SemanticSyntaxVerifierKind.ObjectCreation,
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
            if (node is ImplicitObjectCreationExpressionSyntax
                || node is ObjectCreationExpressionSyntax creation
                    && IsRequestedType(creation.Type)
                || node is ArrayCreationExpressionSyntax array
                    && IsRequestedType(array.Type.ElementType))
            {
                yield return node;
            }
        }
    }

    private bool IsRequestedType(TypeSyntax type) =>
        TerminalTypeName(type) is { } name
        && name.Equals(Type, StringComparison.Ordinal);

    private static string? TerminalTypeName(TypeSyntax type) =>
        type switch
        {
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            PredefinedTypeSyntax predefined => predefined.Keyword.ValueText,
            NullableTypeSyntax nullable => TerminalTypeName(nullable.ElementType),
            ArrayTypeSyntax array => TerminalTypeName(array.ElementType),
            _ => null,
        };
}
