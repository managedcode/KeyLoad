using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal sealed class AdminNodeObserver(PartitionHost partition, CommandAdmissionGovernor commands,
    HttpAdmissionGovernor http, AdminHttpMetrics metrics, IServiceProvider services, TimeProvider clock)
{
    private const string Busy = "A node storage observation is already in progress.";
    private int scanning;

    internal async Task<AdminNodeSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref scanning, 1, 0) != 0)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, Busy); }
        try
        {
            var node = await services.GetRequiredService<INodeAdministration>().StatusAsync(cancellationToken).ConfigureAwait(false);
            var storage = await Task.Run(() => AdminStorageObserver.Read(partition.DirectoryPath, cancellationToken), cancellationToken).ConfigureAwait(false);
            return new(clock.GetUtcNow(), node, new(commands.Limits, commands.Snapshot()) { Http = http.Status() }, storage, metrics.Snapshot());
        }
        finally
        { Volatile.Write(ref scanning, 0); }
    }
}
