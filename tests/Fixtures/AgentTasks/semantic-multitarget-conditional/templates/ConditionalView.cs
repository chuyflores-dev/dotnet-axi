using SemanticMultiTargetConditional.Models;
namespace SemanticMultiTargetConditional.Consumers;
public sealed class ConditionalView
{
    public string Create(ConditionalFormatter formatter, string value) => $"{formatter.Format(value)}|{formatter.Format(7)}";
}
