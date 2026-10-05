using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipInitialization(ReplicaMembershipStore store, ReplicaConsensus replica,
    IOptions<OrleansMembershipOptions> options, TimeProvider clock, int maximumRows, CancellationToken startupCancellation)
{
    private readonly OrleansMembershipOptions settings = options.Value;

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(settings.StartupTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(startupCancellation, cancellationToken, deadline.Token);
        await replica.TransportReady.WaitAsync(linked.Token).ConfigureAwait(false);
        while (true)
        {
            linked.Token.ThrowIfCancellationRequested();
            try
            {
                await store.ReadAsync(linked.Token).ConfigureAwait(false);
                return;
            }
            catch (KeyLoadException error) when (error.Code is ErrorCode.OwnershipLost or ErrorCode.UnknownWriteOutcome
                || maximumRows == ReplicaMembershipProtocol.UnboundedRows && error.Code == ErrorCode.ResourceExhausted)
            {
                await Task.Delay(settings.StartupRetryDelay, clock, linked.Token).ConfigureAwait(false);
            }
        }
    }
}
