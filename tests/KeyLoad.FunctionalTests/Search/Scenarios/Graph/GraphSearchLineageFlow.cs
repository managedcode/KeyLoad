using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

internal static class GraphSearchLineageFlow
{
    internal static async Task RunAsync(bool expansion, bool emptyLabels)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var state = new GraphSearchLineageFixture(expansion, emptyLabels);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await state.InitializeAsync();
                await GraphSearchLineageValues.RefusedAsync(state, ErrorCode.PermissionDenied, GraphSearchLineageProtocol.Denied);
                await GraphSearchLineageFault.RunAsync(state);
                await GraphSearchLineageValues.HealthyAsync(state);
                await GraphSearchLineageValues.ReplayAsync(state);
                await state.PolicyAsync(GraphSearchLineageProtocol.WithdrawnEpoch, false);
                await GraphSearchLineageValues.RefusedAsync(state, ErrorCode.PermissionDenied, GraphSearchLineageProtocol.Denied);
                await state.PolicyAsync(GraphSearchLineageProtocol.RepairedEpoch, true);
                await GraphSearchLineageValues.HealthyAsync(state);
                var image = CanonicalStoreImage.Bytes(state.Store);
                var position = state.Store.Position;
                var identity = state.Store.Identity;
                state.Harness.JoinOwnedResources();
                for (var cold = 0; cold < GraphSearchLineageProtocol.ColdCycles; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(() => GraphSearchLineageCold.RunAsync(state, image, position, identity), failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
