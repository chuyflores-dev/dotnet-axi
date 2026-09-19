using SemanticOverloadRelationships.Implementations;

namespace SemanticOverloadRelationships.Consumers;

public sealed class WorkerReportView(WorkerReportFormatter formatter)
{
    public string Render(string value) => formatter.Format(value);

    public string Revision(int revision) => formatter.Format(revision);
}
