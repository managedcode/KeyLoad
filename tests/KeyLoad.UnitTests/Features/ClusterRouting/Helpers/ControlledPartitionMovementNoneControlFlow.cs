using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises the real journal/materializer consumer of malformed persisted control, then repairs and cold-replays it.</summary>
internal static class ControlledPartitionMovementNoneControlFlow
{
    private const int FirstInstallPage = 0;
    private const long IndexStep = 1;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult captured,
        PartitionMoveSourceFenceRecord fence, PartitionMovementCaptureHandle handle,
        string originalCallerAddress, DateTimeOffset originalExpiry,
        Func<PartitionMovementPeerAdmission, Task> healthyContinuation, CancellationToken cancellationToken)
    {
        var request = ControlledPartitionMovementTargetInstallRequest.Authorize(captured, fence, handle,
            FirstInstallPage, corpus, originalCallerAddress, originalExpiry);
        var verified = await ControlledPartitionMovementPeerVerification.VerifyAsync(source, runtime,
            admission, request, cancellationToken);
        var key = KeySpace.Partition(PartitionMoveProtocol.RecordSpace,
            ControlledPartitionMovementCorpus.Partition, ControlledPartitionMovementPrepareRequest.MoveId);
        var originalBytes = source.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The actual persisted control row is absent.");
        var original = NativeSerialization.Deserialize<PartitionMoveControlRecord>(originalBytes);
        var corrupt = NativeSerialization.Serialize(original with { Phase = PartitionMovePhase.None });
        source.Store.Commit((transaction, _) => { transaction.Put(key, corrupt); return true; });
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RejectAsync(source, verified, cancellationToken), failures);
        ServerFailureObserver.Observe(() => source.Store.Commit((transaction, _) =>
            { transaction.Put(key, originalBytes); return true; }), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        var joinedCorruption = source.ReopenAfterExpectedCorruptApply();
        await Assert.That(joinedCorruption.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(joinedCorruption.Message).IsEqualTo(PartitionMoveProtocol.Invalid);
        await ControlledPartitionMovementNoneControlHealthyOwner.ExecuteAsync(source, runtime,
            request, captured, healthyContinuation, cancellationToken);
    }

    private static async Task RejectAsync(ControlledPartitionMovementNode source,
        PartitionMovementTransportRequest verified, CancellationToken cancellationToken)
    {
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var originalIndex = source.Journal.Log.State.LastIndex;
        var operation = source.Database.CreateVerifiedPartitionMovementOperation(verified.CommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, source.Database.EvaluationClock.GetUtcNow(), verified.Envelope);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => source.Journal.Submit(operation, cancellationToken))
            ?? throw new InvalidOperationException("The original faulted materializer wait did not reject.");
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(failure.Message).IsEqualTo(ReplicaProtocol.CorruptLog);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
            .SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(checked(originalIndex + IndexStep));
        await Assert.That(source.Journal.Log.State.CommittedIndex).IsEqualTo(checked(originalIndex + IndexStep));
    }
}
