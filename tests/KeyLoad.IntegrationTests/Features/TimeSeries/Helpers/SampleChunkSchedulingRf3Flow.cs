using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSchedulingRf3Flow
{
    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave, CancellationToken token, bool abrupt = false)
    {
        var originalOwner = wave.Application;
        var prepared = await PrepareAsync(wave, token).ConfigureAwait(false);
        var controls = wave.QueryControls;
        var scenario = prepared.Scenario;
        var originalDiscovery = prepared.Discovery;
        var arm = prepared.Arm;
        var mergeArm = prepared.MergeArm;
        var original = prepared.ReturnedMarker;
        var heldMerge = prepared.MergeMarker;
        var originalJob = prepared.Job;
        if (abrupt)
        {
            await SampleChunkSchedulingRf3OwnerCut.KillAndJoinAsync(wave, originalOwner, [arm, mergeArm], token)
                .ConfigureAwait(false);
        }
        else
        {
            _ = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
            await SampleChunkSchedulingRf3Stages.RequireCancelledAndDisposedAsync(controls,
                originalDiscovery, arm, original, token).ConfigureAwait(false);
            await SampleChunkSchedulingRf3Stages.RequireCancelledAndDisposedAsync(controls,
                originalDiscovery, mergeArm, heldMerge, token).ConfigureAwait(false);
        }
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await using (var cold = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            RequestCqrsRf3Protocol.Node2, scenario.Secret, token).ConfigureAwait(false))
        {
            await SampleChunkNativeJobRf3Log.RequireExecutingAsync(wave, originalJob, token).ConfigureAwait(false);
            var merged = await scenario.Window.WaitForActualMergeAsync(cold.Sdk, token).ConfigureAwait(false);
            await SampleChunkRf3Assertions.CompleteAsync(scenario.Window, merged,
                scenario.Correction ?? throw new InvalidOperationException(SampleChunkJobRevocationProtocol.Missing));
            await SampleChunkJobRevocationAssertions.HealthyAsync(scenario, cold.Sdk, cold.Mcp, token).ConfigureAwait(false);
        }
        await SampleChunkSchedulingRf3HealthyContinuation.ExecuteAsync(wave, scenario, token).ConfigureAwait(false);
    }
    private static async Task<SampleChunkSchedulingRf3Prepared> PrepareAsync(TwoRf3MembershipWave wave,
        CancellationToken token)
    {
        var controls = wave.QueryControls;
        var originalDiscovery = await SampleChunkSchedulingRf3Stages.DiscoveryAsync(wave, token).ConfigureAwait(false);
        SampleChunkJobRevocationScenario scenario;
        Guid arm; Guid mergeArm;
        SampleChunkNativeJobRf3Receipt originalJob;
        RequestCqrsProbeMarkerRecord original; RequestCqrsProbeMarkerRecord heldMerge;
        await using (var administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            RequestCqrsRf3Protocol.Node1, wave.Profile.AdminKey, token).ConfigureAwait(false))
        {
            scenario = await SampleChunkJobRevocationScenario.CreateAsync(administrator.Sdk, token).ConfigureAwait(false);
            await using var creator = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
                RequestCqrsRf3Protocol.Node2, scenario.Secret, token).ConfigureAwait(false);
            await scenario.SeedAsync(creator.Sdk, administrator.Sdk, token).ConfigureAwait(false);
            var command = SampleChunkJobProbeIdentity.OriginalMerge(scenario);
            arm = controls.WriteArm(scenario.Creator.Id, command, null,
                RequestCqrsProbePhase.SampleChunkNativeJobReturned, RequestCqrsProbeAction.Hold);
            mergeArm = controls.WriteArm(scenario.Creator.Id, command, null,
                RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold, sourceArmId: arm);
            await SampleChunkSchedulingPairRf3Controls.RequireAsync(controls, arm, mergeArm).ConfigureAwait(false);
            scenario.Correction = await scenario.Window.CommitAsync(administrator.Sdk,
                new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                    [scenario.Window.Late], TimeSeriesRf3Scenario.PrivateTags), token).ConfigureAwait(false);
            original = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.SampleChunkNativeJobReturned,
                RequestCqrsProbeOutcome.Observed, originalDiscovery, token).ConfigureAwait(false);
            await Assert.That(original.CommandId).IsEqualTo(command);
            heldMerge = await controls.WaitForMarkerAsync(mergeArm, RequestCqrsProbePhase.AuthorizationReload,
                RequestCqrsProbeOutcome.Observed, originalDiscovery, token).ConfigureAwait(false);
            await Assert.That(heldMerge.CommandId).IsEqualTo(command);
            await Assert.That(heldMerge.RequestId != original.RequestId).IsTrue();
            originalJob = await SampleChunkNativeJobRf3Log.ReturnedAsync(wave, command, token).ConfigureAwait(false);
            await SampleChunkSchedulingRf3Assertions.PendingAsync(scenario, creator.Sdk, creator.Mcp, token)
                .ConfigureAwait(false);
        }
        return new(scenario, originalDiscovery, arm, mergeArm, original, heldMerge, originalJob);
    }
}
