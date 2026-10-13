using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class PostgresControlFlush
{
    internal static async Task RunAsync(IServiceProvider services, PostgresWireWriter writer, CancellationToken closed)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new PostgresTransportDeadline(
                TimeSpan.FromSeconds(services.GetRequiredService<IOptions<DatabaseLimits>>().Value.QueryDeadlineSeconds),
                services.GetRequiredService<TimeProvider>(), closed);
            await ServerFailureObserver.ObserveAsync(() => writer.FlushAsync(deadline.Token).AsTask(),
                failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
