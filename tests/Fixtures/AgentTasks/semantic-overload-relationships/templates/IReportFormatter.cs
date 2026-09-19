namespace SemanticOverloadRelationships.Contracts;

public interface IReportFormatter
{
    string Format(string value);

    string Format(int revision);
}
