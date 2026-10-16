using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Retains every native family and page while rejecting invalid proofs before any capture effect.</summary>
internal static class ControlledPartitionMovementNativeHostedCapture
{
    private const int FirstPageOrdinal = 0;

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMovePhaseResult prepared, CancellationToken token)
    {
        var source = owners.Source;
        var corpus = new ControlledPartitionMovementLoopbackCorpus(owners);
        var expiry = source.Clock.GetUtcNow() + source.Runtime.GrainRouting.Value.RequestLifetime;
        var fence = await ControlledPartitionMovementNativeCaptureFence.ExecuteAsync(owners, prepared, corpus, expiry, token);
        var authorization = await ControlledPartitionMovementNativeOuterExchange.ExecuteAsync<PartitionMovePhaseResult>(owners,
            ControlledPartitionMovementCaptureRequest.Authorize(source.Database, source.Runtime, fence, corpus,
                source.CallerSiloAddress, expiry), token);
        var request = ControlledPartitionMovementCaptureRequest.Capture(source.Database, source.Runtime, fence,
            authorization, corpus, source.CallerSiloAddress, expiry);
        await ControlledPartitionMovementNativeProofFlow.RejectAsync(source, owners.Target, owners.Control, request, token);
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetBefore = ControlledPartitionMovementRawImage.Bytes(owners.Target.Store);
        var position = source.Store.Position;
        var index = source.Log.State.LastIndex;
        var targetPosition = owners.Target.Store.Position;
        var targetIndex = owners.Target.Log.State.LastIndex;
        var handle = await ControlledPartitionMovementNativeOuterExchange.ExecuteAsync<PartitionMovementCaptureHandle>(owners, request, token);
        await Assert.That(NativeSerialization.Measure(handle)).IsLessThanOrEqualTo(source.Database.Limits.MaxBatchBytes);
        await ControlledPartitionMovementResourceAssertions.CaptureAsync(handle.Descriptor);
        await Assert.That(handle.ExpiresAt).IsEqualTo(expiry);
        var pages = new PartitionMovementPageResult[handle.PageCount];
        for (var ordinal = FirstPageOrdinal; ordinal < pages.Length; ordinal++)
        {
            pages[ordinal] = await ControlledPartitionMovementNativeOuterExchange.ExecuteAsync<PartitionMovementPageResult>(owners,
                request with
                {
                    Action = PartitionMovementTransportAction.Page,
                    HandleId = handle.HandleId,
                    Ordinal = ordinal
                }, token);
            await Assert.That(pages[ordinal].HandleId).IsEqualTo(handle.HandleId);
            await Assert.That(pages[ordinal].Ordinal).IsEqualTo(ordinal);
        }
        await ControlledPartitionMovementCapturedPages.AssertAsync(before, handle, pages);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(before)).IsTrue();
        await Assert.That(owners.Target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(owners.Target.Log.State.LastIndex).IsEqualTo(targetIndex);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(owners.Target.Store).SequenceEqual(targetBefore)).IsTrue();
        var settled = await ControlledPartitionMovementNativeOuterExchange.ExecuteAsync<PartitionMovePhaseResult>(owners,
            request with
            {
                Action = PartitionMovementTransportAction.Release,
                HandleId = handle.HandleId
            }, token);
        await Assert.That(settled.Journal.CommandId).IsEqualTo(ControlledPartitionMovementCaptureRequest.CaptureCommandId);
        await Assert.That(settled.Journal.AppliedPosition).IsEqualTo(source.Log.State.LastIndex);
        return settled;
    }
}
