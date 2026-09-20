using SemanticImpact.Models;

namespace SemanticImpact.Consumers;

public sealed class ImpactView
{
    public string Create(ImpactFormatter formatter, string value) => $"{formatter.Format(value)}|{formatter.Format(7)}";
}
