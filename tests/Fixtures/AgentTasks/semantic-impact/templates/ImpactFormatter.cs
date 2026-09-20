namespace SemanticImpact.Models;

public sealed class ImpactFormatter
{
    private int stringCallCount;

    public string Format(string value) => $"impact:{value}:{++stringCallCount}";
    public string Format(int value) => $"number:{value}";
}
