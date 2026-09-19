using SemanticOverloadRelationships.Contracts;

namespace SemanticOverloadRelationships.Consumers;

public sealed class ReportView(IReportFormatter formatter)
{
    public string Render(string value) => formatter.Format(value);

    public string Revision(int revision) => formatter.Format(revision);
}
