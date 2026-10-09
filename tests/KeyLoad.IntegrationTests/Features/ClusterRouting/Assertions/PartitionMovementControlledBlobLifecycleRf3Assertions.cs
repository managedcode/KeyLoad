using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementControlledBlobLifecycleRf3Assertions
{
    internal static async Task RequireReceiptAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveResult terminal, Guid commandId, CommitReceipt receipt)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(terminal.DestinationOwner.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(terminal.PublishedPlacement!.PlacementEpoch);
    }

    internal static async Task RequireStoredAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementControlledBlobCommandRf3Proof[] proofs, PartitionMovementPublicParentRf3NativeCut[] cuts)
    {
        var source = cuts.Where(value => value.Header is not null).ToArray();
        await Assert.That(source.Length).IsEqualTo(seed.Directory.ControlOwner.VoterIds.Length);
        foreach (var cut in source)
        {
            foreach (var proof in proofs)
            { await RequireOneAsync(seed, proof, cut); }
        }
    }

    private static async Task RequireOneAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementControlledBlobCommandRf3Proof proof, PartitionMovementPublicParentRf3NativeCut cut)
    {
        var identity = new PartitionControlCommandIdentity(CommandOutcomeScopeKind.Partition, seed.Partition,
            PartitionMovementPublicParentRf3Administrator.PrincipalId, proof.CommandId);
        var record = PartitionMovementCapturePointerRf3Fault.Read<PartitionControlCommandRecord>(cut,
            PartitionControlCommandKeys.Authority(identity));
        PartitionControlCommandValidation.Require(record, identity);
        await Assert.That(record.Phase).IsEqualTo(PartitionControlCommandPhase.Finalized);
        await Assert.That(record.OriginalOperation!.Kind).IsEqualTo(proof.Kind);
        await Assert.That(record.OriginalResult!.Error).IsEqualTo(proof.Error);
        await Assert.That(record.BlobAuthority is not null).IsEqualTo(proof.HasAuthority);
        await Assert.That(record.TargetEffect!.Token.Incarnation).IsEqualTo(
            seed.Directory.Owners.Single(entry => entry.Owner.PhysicalShardId == seed.FirstRequest.DestinationPhysicalShardId).Owner.Incarnation);
        if (proof.Error is not null)
        { await Assert.That(record.TargetEffect.Mutations.IsEmpty).IsTrue(); }
        var original = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(cut, PartitionControlCommandKeys.Original(identity));
        await Assert.That(original.Result.Error).IsEqualTo(proof.Error);
        await Assert.That(original.BlobAuthority is not null).IsEqualTo(proof.HasAuthority);
        await Assert.That(original.Fingerprint).IsEqualTo(record.Fingerprint);
        await Assert.That(original.PolicyEpoch).IsEqualTo(PartitionMovementPublicParentRf3Administrator.InitialDefinition().PolicyEpoch);
        await Assert.That(original.Incarnation).IsEqualTo(seed.Directory.ControlOwner.Incarnation);
        await SqlRf3Protocol.EqualAsync(seed.Partition, original.Partition);
        await SqlRf3Protocol.EqualAsync(proof.Value, original.Result.NativeValue);
    }

    internal static async Task RequireOriginalRowsAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementControlledBlobCommandRf3Proof[] proofs, PartitionMovementPublicParentRf3NativeCut[] before,
        PartitionMovementPublicParentRf3NativeCut[] after)
    {
        foreach (var previous in before.Where(value => value.Header is not null))
        {
            var current = after.Single(value => value.Node == previous.Node);
            foreach (var proof in proofs)
            {
                var identity = new PartitionControlCommandIdentity(CommandOutcomeScopeKind.Partition, seed.Partition,
                    PartitionMovementPublicParentRf3Administrator.PrincipalId, proof.CommandId);
                foreach (var key in new[] { PartitionControlCommandKeys.Authority(identity), PartitionControlCommandKeys.Original(identity) })
                {
                    var encoded = Convert.ToHexString(key);
                    await Assert.That(PartitionMovementCapturePointerRf3Fault.Rows(current)[encoded])
                        .IsEqualTo(PartitionMovementCapturePointerRf3Fault.Rows(previous)[encoded]);
                }
            }
        }
    }

    internal static async Task RequireCallsAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementControlledBlobCommandRf3Proof[] proofs, ErrorCode? overrideError, CancellationToken token)
    {
        foreach (var proof in proofs)
        {
            var error = overrideError ?? (proof.LifetimeRemoved ? ErrorCode.TokenInvalidated : proof.Error);
            await PartitionMovementControlledBlobLifecycleRf3Calls.RequireAsync(seed, proof, error, token).ConfigureAwait(false);
        }
    }
}
