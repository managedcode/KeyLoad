using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3Publication
{
    internal static async Task RequireAsync(AnnMaintenanceResult actual, AnnMaintenanceRequest request)
    {
        await Assert.That(actual.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(actual.Consumer).IsEqualTo(request.Consumer);
        await Assert.That(actual.IndexGeneration).IsEqualTo(request.IndexGeneration);
        await Assert.That(actual.Phase).IsEqualTo(AnnMaintenancePhase.Completed);
        await Assert.That(actual.Count).IsEqualTo(AdaptiveFilteredPublicRf3Protocol.Count);
        await Assert.That(actual.Source).IsNotNull();
        await Assert.That(actual.Source!.NodeId).IsEqualTo(request.NodeId);
        await Assert.That(actual.Source.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await Assert.That(actual.IndexSha256).IsNotNull();
        await Assert.That(actual.Checkpoint).IsNotNull();
    }
    internal static async Task<AnnMaintenanceRequest> RestoreAsync(RequestCqrsRf3Callers administrator,
        AdaptiveFilteredPublicRf3State state, CancellationToken token)
    {
        var current = await state.Scenario.RequestAsync(administrator.Sdk, administrator.Mcp, token);
        await AdaptiveFilteredPublicRf3Equal.RequireAsync(state.Pin.Placement, current.Placement);
        await Assert.That(current.NodeId).IsEqualTo(state.Pin.NodeId);
        var restore = current with
        {
            Mode = AnnMaintenanceMode.Restore
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.MaintainAnnIndexAsync(restore, token));
        await RequireAsync(actual, restore);
        await Assert.That(actual.IndexSha256).IsEqualTo(state.Published.IndexSha256);
        await Assert.That(actual.Source!.CorpusSha256).IsEqualTo(state.Published.Source!.CorpusSha256);
        await Assert.That(actual.Source.ThroughSequence).IsEqualTo(state.Published.Source.ThroughSequence);
        await AdaptiveFilteredPublicRf3Equal.RequireAsync(state.Published.Checkpoint, actual.Checkpoint);
        return restore;
    }
}
