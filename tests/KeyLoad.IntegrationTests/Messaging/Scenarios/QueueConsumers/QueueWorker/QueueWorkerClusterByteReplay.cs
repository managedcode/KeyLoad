using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueWorkerClusterByteReplay
{
    internal static async Task ReceiveAsync(KeyLoadClient admin, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerClusterByteSeed seed, QueueWorkerOperationObservation original, Delivery delivery, CancellationToken token)
    {
        var expected = new ReceiveResult(original.Receive!.RequestId, [delivery], original.ReceiveToken!);
        await QueueWorkerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ReceiveAsync(original.Receive, token)), expected);
        await QueueWorkerClusterByteImages.RequireAsync(admin, sdk, mcp, seed, delivery, false, token);
        await QueueWorkerClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(
            McpCallerTools.MessagesReceive, original.Receive, token))).Value, expected);
        await QueueWorkerClusterByteImages.RequireAsync(admin, sdk, mcp, seed, delivery, false, token);
        await QueueWorkerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CommitAsync(seed.Execution.Producer, token)), seed.Execution.Receipt);
        await QueueWorkerClusterByteImages.RequireAsync(admin, sdk, mcp, seed, delivery, false, token);
    }
}
