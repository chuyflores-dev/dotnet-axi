using SemanticImpact.Models;

namespace SemanticImpact.CandidateTests;

public static class ImpactFormatterTests
{
    public static string Verify(ImpactFormatter formatter, string value) => $"{formatter.Format(value)}|{formatter.Format(7)}";
}
