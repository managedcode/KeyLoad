namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireFailureDeadline : IDisposable
{
    private readonly CancellationTokenSource timeout;
    internal CancellationTokenSource Source { get; }
    internal CancellationToken Token => Source.Token;

    internal AspireFailureDeadline(TimeSpan duration, CancellationToken caller)
    {
        timeout = new(duration, TimeProvider.System);
        try
        {
            Source = CancellationTokenSource.CreateLinkedTokenSource(caller, timeout.Token);
        }
        catch (Exception)
        {
            timeout.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Source.Dispose();
        timeout.Dispose();
    }
}
