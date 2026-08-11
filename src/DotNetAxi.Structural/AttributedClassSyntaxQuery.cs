using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetAxi.Structural;

/// <summary>Finds class declarations with a matching syntactic attribute name.</summary>
public sealed class AttributedClassSyntaxQuery : ISemanticallyVerifiableSyntaxQuery
{
    private const string AttributeSuffix = "Attribute";

    public AttributedClassSyntaxQuery(string attributeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        if (attributeName.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An attribute name cannot contain a null character.",
                nameof(attributeName));
        }

        AttributeName = attributeName;
        var normalizedAttributeName = WithoutOptionalAttributeSuffix(attributeName);
        Identity = "attributed-class/v1/attribute/"
            + Convert.ToHexStringLower(Encoding.UTF8.GetBytes(normalizedAttributeName));
    }

    public string AttributeName { get; }

    public string Kind => "class";

    public string Identity { get; }

    public SemanticSyntaxVerifier Verifier => new(
        SemanticSyntaxVerifierKind.AttributedClass,
        AttributeName);

    public IEnumerable<SyntaxNode> FindCandidates(
        CompilationUnitSyntax root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        cancellationToken.ThrowIfCancellationRequested();

        var requestedName = WithoutOptionalAttributeSuffix(AttributeName);
        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is ClassDeclarationSyntax declaration
                && HasMatchingAttribute(declaration, requestedName, cancellationToken))
            {
                yield return declaration;
            }
        }
    }

    private static bool HasMatchingAttribute(
        ClassDeclarationSyntax declaration,
        string requestedName,
        CancellationToken cancellationToken)
    {
        foreach (var list in declaration.AttributeLists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var attribute in list.Attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TerminalName(attribute.Name) is { } terminalName
                    && WithoutOptionalAttributeSuffix(terminalName)
                        .Equals(requestedName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? TerminalName(NameSyntax name) =>
        name switch
        {
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            _ => null,
        };

    private static string WithoutOptionalAttributeSuffix(string name) =>
        name.Length > AttributeSuffix.Length
        && name.EndsWith(AttributeSuffix, StringComparison.Ordinal)
            ? name[..^AttributeSuffix.Length]
            : name;
}
