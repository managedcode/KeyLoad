using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextCapturedRf3BuildWindow
{
    internal static async Task<(NativeTextMaintenanceOriginalMutation Mutation, OnlineTextIndexMaintenanceResult Result)>
        CompleteAsync(RequestCqrsProbeFixture probe, IReadOnlyList<ReplicaSiloDiscovery> discovery,
        RequestCqrsRf3Callers active, NativeTextMaintenanceRf3Scenario seed,
        OnlineTextIndexMaintenanceRequest request, Guid arm, Task<OnlineTextIndexMaintenanceResult> original,
        CancellationToken token)
    {
        var marker = await probe.WaitForMarkerAsync(arm, RequestCqrsProbePhase.OnlineTextCaptured,
            RequestCqrsProbeOutcome.Observed, discovery, token);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, arm, request.CommandId,
            RequestCqrsProbePhase.OnlineTextCaptured, discovery);
        var mutation = await NativeTextMaintenanceRf3Mutation.CommitAsync(active.Sdk, seed, token);
        probe.WriteRelease(arm, marker.RequestId);
        var result = await original;
        await NativeTextOnlineRf3Assertions.ResultAsync(result, request, true);
        await Assert.That(result.PublishedCut.ThroughSequence).IsGreaterThan(result.BaseCut.ThroughSequence);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(probe, arm, marker.RequestId,
            request.CommandId, discovery, token);
        await probe.RetireArmAsync(arm, token);
        return (mutation, result);
    }
}
