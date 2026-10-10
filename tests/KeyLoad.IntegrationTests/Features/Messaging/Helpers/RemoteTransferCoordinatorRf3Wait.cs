using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferCoordinatorRf3Wait
{
    internal static async Task<QueueTransferInspection> DeliveredAsync(KeyLoadClient sdk, RemoteTransferColdSeed seed,
        CancellationToken token)
    {
        var deadline = TimeProvider.System.GetUtcNow() + DueRecurringRf3Protocol.ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < deadline)
        {
            var current = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferAsync(seed.SourceRequest, token));
            if (current?.State == QueueTransferState.Delivered)
            { return current; }
            await Task.Delay(DueRecurringRf3Protocol.PollInterval, TimeProvider.System, token);
        }
        throw new TimeoutException(RemoteTransferCoordinatorRf3Protocol.Missing);
    }
}
