using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3ByteReplay
{
    internal static async Task ReceiveAsync(KeyLoadClient admin, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerRf3ByteSeed seed, QueueWorkerOperationObservation original, Delivery delivery, CancellationToken token)
    {
        var expected = new ReceiveResult(original.Receive!.RequestId, [delivery], original.ReceiveToken!);
        await QueueWorkerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ReceiveAsync(original.Receive, token)), expected);
        await QueueWorkerRf3ByteImages.RequireAsync(admin, sdk, mcp, seed, delivery, false, token);
        await QueueWorkerRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(
            McpCallerTools.MessagesReceive, original.Receive, token))).Value, expected);
        await QueueWorkerRf3ByteImages.RequireAsync(admin, sdk, mcp, seed, delivery, false, token);
        await QueueWorkerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CommitAsync(seed.Native.Producer, token)), seed.Native.Receipt);
        await QueueWorkerRf3ByteImages.RequireAsync(admin, sdk, mcp, seed, delivery, false, token);
    }
}
