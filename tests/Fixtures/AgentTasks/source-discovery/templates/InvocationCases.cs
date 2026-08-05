namespace BenchmarkFixture.Cases;

internal sealed class InvocationCases
{
    public void RecordArchive()
    {
        Telemetry.Record("primary");
        BenchmarkFixture.Telemetry.Record("qualified");
        var mention = "Telemetry.Record(\"text\")";
    }
}
