using SemanticUnrelatedNames.Canonical.Contracts;
namespace SemanticUnrelatedNames.Canonical.Consumers;
public sealed class StatusView(IStatusFormatter formatter) { public string Create(string value) => formatter.Format(value); }
