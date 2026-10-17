using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

internal static class QueueWorkerPolicy
{
    [ConfigurationBinding]
    internal static IOptions<QueueWorkerOptions> Snapshot(IOptions<QueueWorkerOptions> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);
        var original = configured.Value;
        var snapshot = new QueueWorkerOptions
        {
            MaximumConcurrentHandlers = original.MaximumConcurrentHandlers,
            MaximumDeliveries = original.MaximumDeliveries,
            MaximumReceiveCalls = original.MaximumReceiveCalls,
            MaximumRenewalCalls = original.MaximumRenewalCalls,
            MaximumInFlightBytes = original.MaximumInFlightBytes,
            MaximumReceiveBytes = original.MaximumReceiveBytes,
            LeaseSeconds = original.LeaseSeconds,
            RenewalInterval = original.RenewalInterval,
            EmptyPollDelay = original.EmptyPollDelay,
            UncertaintyRetries = original.UncertaintyRetries
        };
        snapshot.Validate();
        return Options.Create(snapshot);
    }
}
