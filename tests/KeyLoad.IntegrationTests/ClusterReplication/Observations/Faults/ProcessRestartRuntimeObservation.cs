using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Observes the actual replacement started by the retained native Aspire command.</summary>
internal static class ProcessRestartRuntimeObservation
{
    internal static async Task<ProcessFaultCommandProtocol.Identity> ReadAsync(DistributedApplication app,
        ProcessRuntimeKillReceipt receipt, CancellationToken cancellationToken)
    {
        var before = ProcessRuntimeProtocol.Identity(receipt.Before);
        await foreach (var update in app.ResourceNotifications.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            if (update.Resource.Name != receipt.ResourceName || update.Resource is not ProjectResource
                || update.Snapshot.State?.Text != KnownResourceStates.Running
                || update.Snapshot.StartTimeStamp is not { } started
                || started.ToUniversalTime().Ticks == before.AspireStartTimeUtcTicks)
            {
                continue;
            }
            var actual = ProjectProcessIdentity.Read(app.ResourceNotifications, receipt.ResourceName);
            if (actual.AspireStartTimeUtcTicks <= before.AspireStartTimeUtcTicks
                || actual.ProcessStartTimeUtcTicks <= before.ProcessStartTimeUtcTicks)
            {
                throw new InvalidOperationException(ProcessRuntimeProtocol.ReplacedFailure);
            }
            return actual;
        }
        throw new InvalidOperationException(ProcessRuntimeProtocol.ReplacedFailure);
    }
}
