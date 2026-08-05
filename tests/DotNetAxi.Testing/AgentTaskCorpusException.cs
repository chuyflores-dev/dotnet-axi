namespace DotNetAxi.Testing;

public sealed class AgentTaskCorpusException : Exception
{
    public AgentTaskCorpusException(string message)
        : base(message)
    {
    }

    public AgentTaskCorpusException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
