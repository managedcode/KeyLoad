using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class AdminNodeObserver(PartitionHost partition, CommandAdmissionGovernor commands,
    HttpAdmissionGovernor http, AdminHttpMetrics metrics, IServiceProvider services, TimeProvider clock,
    IOptions<AdminObservationOptions> observationOptions)
{
    private const string Busy = "A node storage observation is already in progress.";
    private int scanning;

    internal async Task<AdminNodeSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref scanning, 1, 0) != 0)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, Busy);
        }
        try
        {
            var node = await services.GetRequiredService<INodeAdministration>().StatusAsync(cancellationToken);
            var storage = AdminStorageObserver.Read(directory: partition.DirectoryPath, cancellationToken: cancellationToken,
                options: observationOptions, clock: clock);
            return new(clock.GetUtcNow(), node, new(commands.Limits, commands.Snapshot())
            {
                Http = http.Status()
            }, storage, metrics.Snapshot())
            {
                SiloName = services.GetRequiredService<ILocalSiloDetails>().Name,
                ActiveSilos = services.GetRequiredService<IClusterMembershipService>().CurrentSnapshot.Members.Values
                    .Where(member => member.Status == SiloStatus.Active).Select(member => member.Name)
                    .Order(StringComparer.Ordinal).ToImmutableArray()
            };
        }
        finally
        {
            Volatile.Write(ref scanning, 0);
        }
    }
}
