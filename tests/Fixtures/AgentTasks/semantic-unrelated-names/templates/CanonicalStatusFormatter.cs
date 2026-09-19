using SemanticUnrelatedNames.Canonical.Contracts;
namespace SemanticUnrelatedNames.Canonical.Implementations;
public sealed class StatusFormatter : IStatusFormatter { public string Format(string value) => $"canonical:{value}"; }
