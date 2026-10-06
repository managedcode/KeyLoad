using KeyLoad.Core;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class AdminNodeObserver(PartitionHost partition, CommandAdmissionGovernor commands,
    HttpAdmissionGovernor http, AdminHttpMetrics metrics, IServiceProvider services, TimeProvider clock,
    IOptions<AdminObservationOptions> observationOptions)
{
    private const int IdleObservation = 0;
    private const int ActiveObservation = 1;
    private const string Busy = "A node storage observation is already in progress.";
    private int scanning;

    internal async Task<AdminNodeSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref scanning, ActiveObservation, IdleObservation) != IdleObservation)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, Busy); }
        try
        {
            var node = await services.GetRequiredService<INodeAdministration>().StatusAsync(cancellationToken).ConfigureAwait(false);
            var storage = await Task.Run(() => AdminStorageObserver.Read(directory: partition.DirectoryPath, cancellationToken: cancellationToken,
                options: observationOptions, clock: clock), cancellationToken).ConfigureAwait(false);
            return new(clock.GetUtcNow(), node, new(commands.Limits, commands.Snapshot()) { Http = http.Status() }, storage, metrics.Snapshot())
            { LocalVoter = partition.Configuration.LocalId, Voters = partition.Configuration.VoterIds };
        }
        finally
        { Volatile.Write(ref scanning, IdleObservation); }
    }
}
