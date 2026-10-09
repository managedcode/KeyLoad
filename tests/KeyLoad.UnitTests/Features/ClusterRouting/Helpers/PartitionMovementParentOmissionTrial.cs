using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Corrupts disposable actual native authority, cold reopens and proves omission never admits an effect.</summary>
internal static class PartitionMovementParentOmissionTrial
{
    internal static async Task BeforePrepareAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, PartitionMovementParentNativeFixture fixture,
        PartitionMoveRequest request, Guid phaseId, PartitionMovePhaseCommand prepare, DateTimeOffset expiry)
    {
        var changed = NativeSerialization.Deserialize<PartitionMovePrepareBody>(prepare.Body.Span)
            with
        { RequireParentCheckpoint = false };
        var bytes = NativeSerialization.Serialize(changed);
        await DeniedAsync(source, target, fixture, phaseId, prepare with
        {
            Body = bytes,
            ControlIntentDigest = Convert.ToHexStringLower(SHA256.HashData(bytes))
        }, expiry, ErrorCode.OwnershipLost);
        await RejectMissingAsync(source, target, fixture, request, phaseId, prepare, expiry,
            removePhase: true, removeHeader: false);
        await RejectMissingAsync(source, target, fixture, request, phaseId, prepare, expiry, removePhase: false);
    }

    internal static async Task AfterPrepareAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, PartitionMovementParentNativeFixture fixture,
        PartitionMoveRequest request, Guid phaseId, PartitionMovePhaseCommand prepare, DateTimeOffset expiry)
    {
        var control = source.Store.Read(view => PartitionMoveControlStorage.ReadHistory(view,
            request.Partition, request.MoveId, source.Database.Limits.MaxBatchBytes));
        await Assert.That(control!.ParentCheckpointRequired).IsTrue();
        await RejectMissingAsync(source, target, fixture, request, phaseId, prepare, expiry, removePhase: true);
        await Assert.That(source.Store.Read(view => PartitionMoveControlStorage.ReadHistory(view,
            request.Partition, request.MoveId, source.Database.Limits.MaxBatchBytes))!.ParentCheckpointRequired).IsTrue();
    }

    private static async Task RejectMissingAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, PartitionMovementParentNativeFixture fixture,
        PartitionMoveRequest request, Guid phaseId, PartitionMovePhaseCommand prepare,
        DateTimeOffset expiry, bool removePhase, bool removeHeader = true)
    {
        byte[][] keys = !removeHeader
            ? [PartitionMoveParentKeys.Phase(request.Partition, request.MoveId, phaseId)]
            : removePhase
            ? [PartitionMoveParentKeys.Header(request.Partition, request.MoveId),
                PartitionMoveParentKeys.Active(request.Partition),
                PartitionMoveParentKeys.Phase(request.Partition, request.MoveId, phaseId)]
            : [PartitionMoveParentKeys.Header(request.Partition, request.MoveId),
                PartitionMoveParentKeys.Active(request.Partition)];
        var originals = source.Store.Read(view => keys.Select(view.ReadOwnedValue).ToArray());
        if (originals.Any(static value => value is null))
        { throw new InvalidOperationException("The actual parent omission fixture has no original authority."); }
        source.Store.Commit((transaction, _) =>
        {
            foreach (var key in keys)
            { transaction.Delete(key); }
            return true;
        });
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            source.Reopen();
            await DeniedAsync(source, target, fixture, phaseId, prepare, expiry);
            var changed = NativeSerialization.Deserialize<PartitionMovePrepareBody>(prepare.Body.Span)
                with
            { RequireParentCheckpoint = false };
            var bytes = NativeSerialization.Serialize(changed);
            await DeniedAsync(source, target, fixture, phaseId, prepare with
            {
                Body = bytes,
                ControlIntentDigest = Convert.ToHexStringLower(SHA256.HashData(bytes))
            }, expiry);
        }, failures);
        ServerFailureObserver.Observe(() =>
        {
            source.Store.Commit((transaction, _) =>
            {
                for (var index = 0; index < keys.Length; index++)
                { transaction.Put(keys[index], originals[index]!); }
                return true;
            });
        }, failures);
        ServerFailureObserver.Observe(source.Reopen, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task DeniedAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, PartitionMovementParentNativeFixture fixture,
        Guid phaseId, PartitionMovePhaseCommand prepare, DateTimeOffset expiry,
        ErrorCode expected = ErrorCode.RecoveryRequired)
    {
        var beforeSource = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        var beforeTarget = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        var denied = await Assert.ThrowsAsync<KeyLoadException>(() => fixture.OriginalAsync(phaseId, prepare, expiry));
        await Assert.That(denied!.Code).IsEqualTo(expected);
        await beforeSource.AssertUnchangedAsync(source);
        await beforeTarget.AssertUnchangedAsync(target);
    }
}
