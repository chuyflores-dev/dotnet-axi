using SemanticPartialLinked;
namespace SemanticPartialLinked.Primary;
public sealed class PrimaryView
{
    public string Create(LinkedFormatter formatter, string value) => $"primary:{formatter.Format(value)}|{formatter.Format(7)}";
}
