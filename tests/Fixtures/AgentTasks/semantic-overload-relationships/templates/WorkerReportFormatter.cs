using SemanticOverloadRelationships.Contracts;

namespace SemanticOverloadRelationships.Implementations;

public sealed class WorkerReportFormatter : IReportFormatter
{
    public string Format(string value) => $"worker:{value}";

    public string Format(int revision) => $"worker-number:{revision}";
}
