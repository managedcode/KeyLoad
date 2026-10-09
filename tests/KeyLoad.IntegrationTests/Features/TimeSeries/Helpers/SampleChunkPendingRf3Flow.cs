using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkPendingRf3Flow
{
    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        var state = await PrepareAsync(wave, token).ConfigureAwait(false);
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await SampleChunkPendingRf3Continuation.ResumeAsync(wave, state, token).ConfigureAwait(false);
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await using var cold = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            RequestCqrsRf3Protocol.Node2, state.Scope.Secret, token).ConfigureAwait(false);
        await SampleChunkPendingRf3Continuation.AllHealthyAsync(state, cold, token).ConfigureAwait(false);
    }

    private static async Task<SampleChunkPendingRf3State> PrepareAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        var discovery = await SampleChunkSchedulingRf3Stages.DiscoveryAsync(wave, token).ConfigureAwait(false);
        await using var administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            RequestCqrsRf3Protocol.Node1, wave.Profile.AdminKey, token).ConfigureAwait(false);
        var scope = await SampleChunkJobRevocationScenario.CreateAsync(administrator.Sdk, token).ConfigureAwait(false);
        await using var creator = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            RequestCqrsRf3Protocol.Node2, scope.Secret, token).ConfigureAwait(false);
        var admitted = ImmutableArray.CreateBuilder<SampleChunkPendingRf3Item>(SampleChunkPendingRf3Protocol.NativeAdmissionCapacity);
        for (var ordinal = SampleChunkPendingRf3Protocol.FirstIndex;
            ordinal < SampleChunkPendingRf3Protocol.NativeAdmissionCapacity; ordinal++)
        {
            var item = await SampleChunkPendingRf3Seed.CreateAsync(scope, ordinal, creator.Sdk, administrator.Sdk, token)
                .ConfigureAwait(false);
            await Assert.That(item.Partition).IsEqualTo(scope.Window.Native.Partition);
            await SampleChunkPendingRf3Admission.AdmitUnscheduledAsync(wave, item, administrator.Sdk,
                discovery, token).ConfigureAwait(false);
            admitted.Add(item);
        }
        var candidate = await SampleChunkPendingRf3Seed.CreateAsync(scope, SampleChunkPendingRf3Protocol.CandidateOrdinal,
            creator.Sdk, administrator.Sdk, token).ConfigureAwait(false);
        await SampleChunkPendingRf3Admission.CorrectThenRefusedAsync(wave, candidate, administrator.Sdk, token).ConfigureAwait(false);
        foreach (var item in admitted)
        { await SampleChunkPendingRf3Assertions.PendingAsync(item, creator.Sdk, token).ConfigureAwait(false); }
        await SampleChunkPendingRf3Assertions.CompleteAsync(candidate, creator.Sdk, creator.Mcp, merged: false, token).ConfigureAwait(false);
        return new(scope, admitted.ToImmutable(), candidate);
    }
}
