using SemanticRelationships.Contracts;

namespace SemanticRelationships.Implementations;

public sealed class LedgerFormatter : ILedgerFormatter
{
    public string Format(string value) => $"ledger:{value}";
}
