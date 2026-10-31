namespace KeyLoad.Server;

internal static class PostgresControlFlush
{
    internal static async Task RunAsync(PostgresWireWriter writer, CancellationToken closed)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await writer.FlushAsync(closed), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
