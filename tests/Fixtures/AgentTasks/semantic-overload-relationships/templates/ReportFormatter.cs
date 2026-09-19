using SemanticOverloadRelationships.Contracts;

namespace SemanticOverloadRelationships.Implementations;

public sealed class ReportFormatter : IReportFormatter
{
    public string Format(string value) => $"report:{value}";

    public string Format(int revision) => $"report-number:{revision}";
}
