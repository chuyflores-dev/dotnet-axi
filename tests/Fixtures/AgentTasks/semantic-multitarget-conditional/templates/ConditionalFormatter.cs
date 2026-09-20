namespace SemanticMultiTargetConditional.Models;
public sealed class ConditionalFormatter
{
#if NET8_0
    public string Format(string value) => $"legacy:{value}";
#else
    public string Format(string value) => $"modern:{value}";
#endif
    public string Format(int value) => $"number:{value}";
}
