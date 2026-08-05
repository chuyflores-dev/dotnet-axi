using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetAxi.Structural;

/// <summary>Finds invocation expressions by their terminal syntactic name.</summary>
public sealed class InvocationSyntaxQuery : IRoslynSyntaxQuery
{
    public InvocationSyntaxQuery(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An invocation name cannot contain a null character.",
                nameof(name));
        }

        Name = name;
        Identity = "invocation/v1/name/"
            + Convert.ToHexStringLower(Encoding.UTF8.GetBytes(name));
    }

    public string Name { get; }

    public string Kind => "invocation";

    public string Identity { get; }

    public IEnumerable<SyntaxNode> FindCandidates(
        CompilationUnitSyntax root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is InvocationExpressionSyntax invocation
                && InvokedName(invocation.Expression) is { } invokedName
                && invokedName.Equals(Name, StringComparison.Ordinal))
            {
                yield return invocation;
            }
        }
    }

    private static string? InvokedName(ExpressionSyntax expression) =>
        expression switch
        {
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            _ => null,
        };
}
