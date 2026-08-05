namespace BenchmarkFixture;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class CorpusCaseAttribute : Attribute
{
}

internal sealed class ArchiveClient
{
}

internal sealed class ArchiveClient<T>
{
}

internal static class Telemetry
{
    public static void Record(string value)
    {
    }
}
