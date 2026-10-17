using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RequiredProcessingPolicyRf3Warm
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RequiredProcessingPolicyRf3State state, CancellationToken token)
    {
        var first = await RequiredProcessingPolicyRf3Routes.ReceiveAsync(reader, state, "first", token);
        state.Refused = state.Processing(first, "first");
        await RequiredProcessingPolicyRf3Setup.PolicyAsync(root, state, RequiredProcessingPolicyRf3State.After, token);
        await RequiredProcessingPolicyRf3Values.ObserveAsync(root, state, 0, async () =>
        {
            await RequiredProcessingPolicyRf3Reads.ProjectedAsync(reader, state, token);
            await RequiredProcessingPolicyRf3Refusals.AllAsync(reader, state, state.Refused, RequiredProcessingPolicyRf3State.Denied, token);
        }, token);
        await RequiredProcessingPolicyRf3Setup.PolicyAsync(root, state, RequiredProcessingPolicyRf3State.Before, token);
        await RequiredProcessingPolicyRf3Values.ObserveAsync(root, state, 0, () => RequiredProcessingPolicyRf3Refusals.AllAsync(
            reader, state, state.Refused, RequiredProcessingPolicyRf3State.Denied, token), token);
        state.Original = state.Processing(first, "first");
        state.OriginalReceipt = await RequiredProcessingPolicyRf3Routes.CompleteAsync(root, reader, state, state.Original, "first", token);
        await RequiredProcessingPolicyRf3Setup.PolicyAsync(root, state, RequiredProcessingPolicyRf3State.After, token);
        await RequiredProcessingPolicyRf3Values.ObserveAsync(root, state, 1, () => RequiredProcessingPolicyRf3Refusals.AllAsync(
            reader, state, state.Original, RequiredProcessingPolicyRf3State.Denied, token), token);
        await RequiredProcessingPolicyRf3Setup.PolicyAsync(root, state, RequiredProcessingPolicyRf3State.Before, token);
        await RequiredProcessingPolicyRf3Final.ReplayProcessingAsync(root, reader, state, state.Original, state.OriginalReceipt, 1, token);
        await RequiredProcessingPolicyRf3Setup.PrincipalAsync(root, state, 2L, true, token);
        await RequiredProcessingPolicyRf3Values.ObserveAsync(root, state, 1,
            () => RequiredProcessingPolicyRf3Refusals.RevokedAsync(reader, state, token), token);
        await RequiredProcessingPolicyRf3Setup.PrincipalAsync(root, state, 3L, false, token);
        await RequiredProcessingPolicyRf3Values.ObserveAsync(root, state, 1, () => RequiredProcessingPolicyRf3Refusals.AllAsync(
            reader, state, state.Original, RequiredProcessingPolicyRf3State.EpochChanged, token), token);
        await RequiredProcessingPolicyRf3Setup.EnqueueAsync(root, state, "next", token);
        var next = await RequiredProcessingPolicyRf3Routes.ReceiveAsync(reader, state, "next", token);
        state.Healthy = state.Processing(next, "next");
        await RequiredProcessingPolicyRf3Values.ObserveAsync(root, state, 2,
            () => RequiredProcessingPolicyRf3Refusals.CancelledAsync(reader, state, token), token);
        state.HealthyReceipt = await RequiredProcessingPolicyRf3Routes.CompleteAsync(root, reader, state, state.Healthy, "next", token);
        await RequiredProcessingPolicyRf3Final.RunAsync(root, reader, state, token);
    }
}
