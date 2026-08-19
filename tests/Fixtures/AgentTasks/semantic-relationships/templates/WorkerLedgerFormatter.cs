using SemanticRelationships.Contracts;

namespace SemanticRelationships.Implementations;

public sealed class WorkerLedgerFormatter : ILedgerFormatter
{
    public string Format(string value) => $"worker:{value}";
}
