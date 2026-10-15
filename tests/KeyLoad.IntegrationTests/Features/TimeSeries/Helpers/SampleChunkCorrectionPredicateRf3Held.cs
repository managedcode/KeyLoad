using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Assertions;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkCorrectionPredicateRf3Held
{
    internal static async Task<SampleChunkCorrectionPredicateRf3State> RunAsync(TwoRf3MembershipWave wave,
        SampleChunkJobRevocationScenario scenario, RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers creator, CancellationToken token)
    {
        var controls = wave.QueryControls;
        var discovery = new List<KeyLoad.Clustering.ReplicaSiloDiscovery>();
        foreach (var node in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3 })
        {
            discovery.Add(await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application, node, wave.Profile, token));
        }
        var command = SampleChunkJobProbeIdentity.OriginalMerge(scenario);
        var arm = controls.WriteArm(scenario.Creator.Id, command, null,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
        var failures = new List<Exception>();
        SampleChunkCorrectionPredicateRf3State? state = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var correction = new CommandRequest(Guid.NewGuid(), scenario.Window.Native.Partition,
                [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                    [scenario.Window.Late], SampleChunkCorrectionPredicateRf3State.CorrectionTags)]);
            var receipt = await CommitAsync(administrator, creator, scenario, correction, token);
            state = new(scenario, correction, receipt);
            var held = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.AuthorizationReload,
                RequestCqrsProbeOutcome.Observed, discovery, token);
            await Assert.That(held.CommandId).IsEqualTo(command);
            state.Outbox = await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.OutboxStatusAsync(state.Window.Native.Partition, token));
            await SampleChunkCorrectionPredicateRf3Reads.WaveAsync(administrator, creator, state, 1L, 5L, token);
            controls.WriteRelease(arm, held.RequestId);
            await RequestCqrsAuthorityFaultLifecycleAssertions.VerifyReleasedAndDisposedAsync(
                controls, discovery, arm, command, held, token);
            await controls.RetireArmAsync(arm, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => controls.ReleaseOpenArmsAsync(discovery, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        var original = state ?? throw new InvalidOperationException(SampleChunkJobRevocationProtocol.Missing);
        var merged = await original.Window.WaitForActualMergeAsync(creator.Sdk, token);
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(merged,
            original.Expected(2L, 6L, merged.CutPosition, original.Rows));
        original.Outbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(original.Window.Native.Partition, token));
        await SampleChunkCorrectionPredicateRf3Reads.WaveAsync(administrator, creator, original, 2L, 6L, token);
        return original;
    }
    private static async Task<CommitReceipt> CommitAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers creator, SampleChunkJobRevocationScenario scenario, CommandRequest command,
        CancellationToken token)
    {
        var owner = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(
            new(SampleChunkRf3Protocol.PlacementVersion, command.Partition), token));
        var actual = await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.CommitAsync(command, token));
        await Assert.That(actual.Token.Position).IsGreaterThan(scenario.OriginalReceipt!.Token.Position);
        var expected = new CommitReceipt(command.CommandId, new(owner.Incarnation, command.Partition.AtomicPartitionId,
            actual.Token.Position, owner.PlacementEpoch),
            [new("appendSamples", TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, 3L)],
            DurabilityProfile.QuorumProcessDurable);
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(actual, expected);
        return actual;
    }
}
