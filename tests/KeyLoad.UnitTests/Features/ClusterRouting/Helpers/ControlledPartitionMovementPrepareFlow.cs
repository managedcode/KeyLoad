using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises the original admitted prepare, immutable replay and exact conflicting identity.</summary>
internal static class ControlledPartitionMovementPrepareFlow
{
    private const long ChangedPlacementRevision = 1;
    internal static async Task<(string[] Source, string[] Target, long Position, long Index,
        PartitionMovePhaseResult Prepared, DateTimeOffset ExpiresAt)> ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackCorpus corpus,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission, string callerAddress,
        CancellationToken cancellationToken)
    {
        var placement = ControlledPartitionMovementPrepareAssertions.Placement(corpus);
        var request = ControlledPartitionMovementPrepareRequest.Create(source, runtime, corpus, placement, callerAddress);
        var unconfigured = await ControlledPartitionMovementPrepareAdmission.RejectAsync(source, target,
            runtime, admission, request, cancellationToken);
        var result = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            request, cancellationToken);
        var prepared = await ControlledPartitionMovementPrepareAssertions.PreparedAsync(result, corpus);
        await ControlledMovementOwnerAdmissionAssertions.RejectRetainedReadAsync(unconfigured, source);
        await ControlledMovementOwnerAdmissionAssertions.RejectRestorationAsync(source);
        var receipt = NativeSerialization.Serialize(result);
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var position = source.Store.Position;
        var index = source.Journal.Log.State.LastIndex;
        var replay = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            request with { Envelope = request.Envelope with { Nonce = Guid.NewGuid() } }, cancellationToken);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(receipt)).IsTrue();
        await UnchangedAsync(source, target, sourceImage, targetImage, position, index);
        var changed = ControlledPartitionMovementPrepareRequest.Create(source, runtime, corpus, placement,
            callerAddress, expectedRevision: ChangedPlacementRevision);
        var conflict = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            changed, cancellationToken);
        await ControlledPartitionMovementPrepareAssertions.ConflictAsync(conflict);
        await UnchangedAsync(source, target, sourceImage, targetImage, position, index);
        return (sourceImage, targetImage, position, index, prepared, request.Envelope.ExpiresAt);
    }

    internal static async Task UnchangedAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, string[] sourceImage, string[] targetImage, long position, long index)
    {
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceImage)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage)).IsTrue();
    }
}
