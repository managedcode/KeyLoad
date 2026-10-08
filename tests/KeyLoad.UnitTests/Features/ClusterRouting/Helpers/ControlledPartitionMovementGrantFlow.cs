using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Obtains the actual persisted A grant and source fence through original admitted native operations.</summary>
internal static class ControlledPartitionMovementGrantFlow
{
    internal static async Task<(PartitionMovePhaseResult Authorization, PartitionMovePhaseResult Fence)>
        ExecuteAsync(ControlledPartitionMovementNode source, ServerRuntimeOptions runtime,
        PartitionMovementPeerAdmission admission, ControlledPartitionMovementLoopbackCorpus corpus,
        PartitionMovePhaseResult actualPrepared, string callerAddress, DateTimeOffset originalExpiry,
        CancellationToken cancellationToken)
    {
        var authorizationRequest = ControlledPartitionMovementFenceRequest.Authorize(actualPrepared,
            corpus, callerAddress, originalExpiry);
        var authorizedOutcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime,
            admission, authorizationRequest, cancellationToken);
        var authorization = await ControlledPartitionMovementGrantAssertions.AuthorizedAsync(authorizedOutcome,
            actualPrepared, corpus, originalExpiry);
        var fenceRequest = ControlledPartitionMovementFenceRequest.Fence(actualPrepared, authorization,
            corpus, callerAddress, originalExpiry);
        var fencedOutcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime,
            admission, fenceRequest, cancellationToken);
        var fence = await ControlledPartitionMovementGrantAssertions.FencedAsync(fencedOutcome,
            actualPrepared, corpus);
        var originalReceipt = NativeSerialization.Serialize(fencedOutcome);
        var originalImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var index = source.Journal.Log.State.LastIndex;
        var replay = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            fenceRequest with { Envelope = fenceRequest.Envelope with { Nonce = Guid.NewGuid() } },
            cancellationToken);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(originalReceipt)).IsTrue();
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(originalImage)).IsTrue();
        return (authorization, fence);
    }
}
