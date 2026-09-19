namespace SemanticUnrelatedNames.Archive;
public sealed class StatusFormatter : IStatusFormatter { public string Format(string value) => $"archive:{value}"; }
