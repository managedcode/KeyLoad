using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class RemoteTransferCoordinatorClusterWait
{
    internal static async Task<QueueTransferInspection> DeliveredAsync(KeyLoadClient sdk, RemoteTransferColdSeed seed,
        CancellationToken token)
    {
        var deadline = TimeProvider.System.GetUtcNow() + DueRecurringClusterProtocol.ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < deadline)
        {
            var current = await PublicReadAssertions.TransferAsync(await sdk.InspectQueueTransferAsync(seed.SourceRequest, token));
            if (current?.State == QueueTransferState.Delivered)
            {
                return current;
            }
            await Task.Delay(DueRecurringClusterProtocol.PollInterval, TimeProvider.System, token);
        }
        throw new TimeoutException(RemoteTransferCoordinatorClusterProtocol.Missing);
    }
}
