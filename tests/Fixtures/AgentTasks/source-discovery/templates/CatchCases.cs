namespace BenchmarkFixture.Cases;

internal sealed class CatchCases
{
    public void Direct()
    {
        try
        {
            throw new TimeoutException();
        }
        catch (TimeoutException)
        {
        }
    }

    public void Qualified()
    {
        try
        {
            throw new System.TimeoutException();
        }
        catch (System.TimeoutException)
        {
        }
    }

    public void Untyped()
    {
        try
        {
        }
        catch
        {
        }
    }
}
