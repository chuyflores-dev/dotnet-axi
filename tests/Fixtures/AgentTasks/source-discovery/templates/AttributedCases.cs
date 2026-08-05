namespace BenchmarkFixture.Cases;

[CorpusCase]
internal sealed class PrimaryCase
{
}

[BenchmarkFixture.CorpusCaseAttribute]
internal sealed class QualifiedCase
{
}

[Obsolete]
internal sealed class AttributeNoise
{
    private const string Mention = "CorpusCase";
}
