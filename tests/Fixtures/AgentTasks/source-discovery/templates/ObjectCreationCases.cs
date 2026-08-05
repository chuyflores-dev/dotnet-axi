namespace BenchmarkFixture.Cases;

internal sealed class ObjectCreationCases
{
    public void Create()
    {
        var direct = new ArchiveClient();
        var qualified = new BenchmarkFixture.ArchiveClient();
        var generic = new ArchiveClient<string>();
        var array = new ArchiveClient[2];
        ArchiveClient inferred = new();
        var mention = "new ArchiveClient()";
    }
}
