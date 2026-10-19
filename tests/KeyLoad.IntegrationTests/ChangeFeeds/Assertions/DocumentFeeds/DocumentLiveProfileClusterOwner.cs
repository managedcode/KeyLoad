using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileClusterOwner
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, DocumentLiveProfileClusterState state,
        Func<Task> operation, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.StatusAsync(token));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var after = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.StatusAsync(token));
            await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
            await Assert.That(after.ProcessId).IsEqualTo(before.ProcessId);
            await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
            await Assert.That(after.Incarnation).IsEqualTo(state.Commands[0].Receipt.Token.Incarnation);
            await Assert.That(after.RoutingReady).IsTrue();
            await Assert.That(before.RoutingReady).IsTrue();
            await Assert.That(after.SiloCount).IsEqualTo(3);
            await Assert.That(before.SiloCount).IsEqualTo(3);
            await Assert.That(after.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
            await Assert.That(before.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
            await Assert.That(after.Position).IsGreaterThanOrEqualTo(before.Position);
            var page = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.QueryAstAsync(state.Query, token));
            await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(state.Commands[^1].Receipt.Token.Position);
            await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
