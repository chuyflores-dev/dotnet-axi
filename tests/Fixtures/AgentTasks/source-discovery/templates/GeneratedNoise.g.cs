namespace BenchmarkFixture.Generated;

[CorpusCase]
internal sealed class GeneratedHandler
{
    public void HandleAuditAsync()
    {
        Telemetry.Record("generated");
        var client = new ArchiveClient();

        try
        {
        }
        catch (TimeoutException)
        {
        }
    }
}
