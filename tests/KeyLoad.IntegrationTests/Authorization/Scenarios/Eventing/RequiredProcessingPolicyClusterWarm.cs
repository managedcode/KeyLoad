using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RequiredProcessingPolicyClusterWarm
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        RequiredProcessingPolicyClusterState state, CancellationToken token)
    {
        var first = await RequiredProcessingPolicyClusterRoutes.ReceiveAsync(reader, state, "first", token);
        state.Refused = state.Processing(first, "first");
        await RequiredProcessingPolicyClusterSetup.PolicyAsync(root, state, RequiredProcessingPolicyClusterState.After, token);
        await RequiredProcessingPolicyClusterValues.ObserveAsync(root, state, 0, async () =>
        {
            await RequiredProcessingPolicyClusterReads.ProjectedAsync(reader, state, token);
            await RequiredProcessingPolicyClusterRefusals.AllAsync(reader, state, state.Refused, RequiredProcessingPolicyClusterState.Denied, token);
        }, token);
        await RequiredProcessingPolicyClusterSetup.PolicyAsync(root, state, RequiredProcessingPolicyClusterState.Before, token);
        await RequiredProcessingPolicyClusterValues.ObserveAsync(root, state, 0, () => RequiredProcessingPolicyClusterRefusals.AllAsync(
            reader, state, state.Refused, RequiredProcessingPolicyClusterState.Denied, token), token);
        state.Original = state.Processing(first, "first");
        state.OriginalReceipt = await RequiredProcessingPolicyClusterRoutes.CompleteAsync(reader, state, state.Original, "first", token);
        await RequiredProcessingPolicyClusterSetup.PolicyAsync(root, state, RequiredProcessingPolicyClusterState.After, token);
        await RequiredProcessingPolicyClusterValues.ObserveAsync(root, state, 1, () => RequiredProcessingPolicyClusterRefusals.AllAsync(
            reader, state, state.Original, RequiredProcessingPolicyClusterState.Denied, token), token);
        await RequiredProcessingPolicyClusterSetup.PolicyAsync(root, state, RequiredProcessingPolicyClusterState.Before, token);
        await RequiredProcessingPolicyClusterFinal.ReplayProcessingAsync(root, reader, state, state.Original, state.OriginalReceipt, 1, token);
        await RequiredProcessingPolicyClusterSetup.PrincipalAsync(root, state, 2L, true, token);
        await RequiredProcessingPolicyClusterValues.ObserveAsync(root, state, 1,
            () => RequiredProcessingPolicyClusterRefusals.RevokedAsync(reader, state, token), token);
        await RequiredProcessingPolicyClusterSetup.PrincipalAsync(root, state, 3L, false, token);
        await RequiredProcessingPolicyClusterValues.ObserveAsync(root, state, 1, () => RequiredProcessingPolicyClusterRefusals.AllAsync(
            reader, state, state.Original, RequiredProcessingPolicyClusterState.EpochChanged, token), token);
        await RequiredProcessingPolicyClusterSetup.EnqueueAsync(root, state, "next", token);
        var next = await RequiredProcessingPolicyClusterRoutes.ReceiveAsync(reader, state, "next", token);
        state.Healthy = state.Processing(next, "next");
        await RequiredProcessingPolicyClusterValues.ObserveAsync(root, state, 2,
            () => RequiredProcessingPolicyClusterRefusals.CancelledAsync(reader, state, token), token);
        state.HealthyReceipt = await RequiredProcessingPolicyClusterRoutes.CompleteAsync(reader, state, state.Healthy, "next", token);
        await RequiredProcessingPolicyClusterFinal.RunAsync(root, reader, state, token);
    }
}
