using KeyLoad.Clustering;
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
                services.GetRequiredService<IOptions<GrainRoutingOptions>>().Value.RequestLifetime,
                services.GetRequiredService<TimeProvider>(), closed);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await writer.FlushAsync(deadline.Token);
            },
                failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
