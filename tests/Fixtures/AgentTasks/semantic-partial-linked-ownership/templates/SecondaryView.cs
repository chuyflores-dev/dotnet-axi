using SemanticPartialLinked;
namespace SemanticPartialLinked.Secondary;
public sealed class SecondaryView
{
    public string Create(LinkedFormatter formatter, string value) => $"secondary:{formatter.Format(value)}|{formatter.Format(7)}";
}
