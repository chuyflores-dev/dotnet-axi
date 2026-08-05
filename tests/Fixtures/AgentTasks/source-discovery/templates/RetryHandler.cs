namespace BenchmarkFixture.Handlers;

internal sealed class RetryHandler
{
    private const string Status = "Archive pipeline ready.";

    public Task HandleRetryAsync() => Task.CompletedTask;
}
