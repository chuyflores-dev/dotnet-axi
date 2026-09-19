namespace SemanticUnrelatedNames.Archive;
public sealed class StatusView(IStatusFormatter formatter) { public string Create(string value) => formatter.Format(value); }
