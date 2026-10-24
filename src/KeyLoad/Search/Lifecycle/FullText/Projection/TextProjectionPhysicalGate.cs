namespace KeyLoad.Server.Search;

internal sealed class TextProjectionPhysicalGate
{
    private readonly Lock sync = new();

    internal T Run<T>(Func<T> operation)
    {
        lock (sync)
        {
            return operation();
        }
    }

    internal void Run(Action operation)
    {
        lock (sync)
        {
            operation();
        }
    }
}
