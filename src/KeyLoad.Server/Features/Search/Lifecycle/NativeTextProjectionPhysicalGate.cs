namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjectionPhysicalGate
{
    private readonly object sync = new();

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
