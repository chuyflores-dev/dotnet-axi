using SemanticRelationships.Contracts;

namespace SemanticRelationships.Consumers;

public sealed class LedgerReport(ILedgerFormatter formatter)
{
    public string Create(string value) => formatter.Format(value);
}
