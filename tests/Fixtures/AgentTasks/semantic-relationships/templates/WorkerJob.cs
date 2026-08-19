using SemanticRelationships.Implementations;

namespace SemanticRelationships.Consumers;

public sealed class WorkerJob(WorkerLedgerFormatter formatter)
{
    public string Run(string value) => formatter.Format(value);
}
