namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedKeyLoadFaultDeadline : IDisposable
{
    private readonly CancellationTokenSource timeout;
    private readonly CancellationTokenSource deadline;
    internal CancellationToken Token => deadline.Token;

    internal IsolatedKeyLoadFaultDeadline(TimeSpan duration, CancellationToken caller)
    {
        timeout = new(duration, TimeProvider.System);
        try
        {
            deadline = CancellationTokenSource.CreateLinkedTokenSource(caller, timeout.Token);
        }
        catch
        {
            timeout.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        deadline.Dispose();
        timeout.Dispose();
    }
}
