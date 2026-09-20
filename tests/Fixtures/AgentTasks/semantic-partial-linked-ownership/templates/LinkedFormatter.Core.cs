namespace SemanticPartialLinked;
public sealed partial class LinkedFormatter
{
    private int callCount;
    public int CallCount => callCount;
    private string Record(string value) => $"linked:{value}:{++callCount}";
    public string Format(int value) => $"number:{value}";
}
