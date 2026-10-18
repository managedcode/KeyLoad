using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLeaseClusterRefusal
{
    internal static async Task ForeignAsync(ClusterFixture fixture, QueueLeaseClusterSeed seed,
        Delivery delivery, KeyLoadClient owner, MessageInspection unchanged, List<Exception> failures, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var foreign = new KeyLoadClient(http, seed.Foreign.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            seed.Foreign.Secret, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            foreach (var action in new[] { DeliveryAction.Ack, DeliveryAction.Renew })
            {
                var command = new DeliveryCommand(Guid.NewGuid(), seed.Lane, delivery.Token, action);
                await QueueLeaseClusterAssertions.DeniedAsync(await foreign.CompleteAsync(command, token), ErrorCode.TokenInvalidated);
                var other = command with
                {
                    CommandId = Guid.NewGuid()
                };
                await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesComplete, other, token),
                    ErrorCode.TokenInvalidated, dispatched: true);
                await QueueLeaseClusterAssertions.EqualAsync(await QueueLeaseClusterAssertions.InspectAsync(owner, seed.Lane, token), unchanged);
            }
        }, failures).ConfigureAwait(false);
    }

    internal static async Task StaleAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueLeaseClusterOriginal original, MessageInspection unchanged, CancellationToken token)
    {
        foreach (var action in new[] { DeliveryAction.Ack, DeliveryAction.Renew })
        {
            var command = new DeliveryCommand(Guid.NewGuid(), original.Seed.Lane, original.Delivery.Token, action);
            await QueueLeaseClusterAssertions.DeniedAsync(await sdk.CompleteAsync(command, token), ErrorCode.StaleLease);
            var other = command with
            {
                CommandId = Guid.NewGuid()
            };
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesComplete, other, token),
                ErrorCode.StaleLease, dispatched: true);
            await QueueLeaseClusterAssertions.EqualAsync(await QueueLeaseClusterAssertions.InspectAsync(sdk, original.Seed.Lane, token), unchanged);
        }
        await QueueLeaseClusterAssertions.DeniedAsync(await sdk.ReceiveAsync(original.Claim, token), ErrorCode.StaleLease);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesReceive, original.Claim, token),
            ErrorCode.StaleLease, dispatched: true);
        await QueueLeaseClusterAssertions.EqualAsync(await QueueLeaseClusterAssertions.InspectAsync(sdk, original.Seed.Lane, token), unchanged);
    }
}
