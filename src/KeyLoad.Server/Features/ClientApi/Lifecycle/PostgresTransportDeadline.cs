namespace KeyLoad.Server;

internal sealed class PostgresTransportDeadline : IDisposable
{
    private readonly CancellationTokenSource deadline;
    private readonly CancellationTokenSource linked;

    internal PostgresTransportDeadline(TimeSpan duration, TimeProvider clock, CancellationToken closed)
    {
        deadline = new CancellationTokenSource(duration, clock);
        try
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(closed, deadline.Token);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(() => { using (deadline) { } }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal CancellationToken Token => linked.Token;

    public void Dispose()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => { using (linked) { } }, failures);
        ServerFailureObserver.Observe(() => { using (deadline) { } }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
