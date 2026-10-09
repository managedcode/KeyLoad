using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementControlledBlobFailedEffectRf3Assertions
{
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3BlobOriginals originals, PartitionMovementPublicParentRf3NativeCut[] cuts)
    {
        var request = originals.FailedPart ?? throw new InvalidOperationException(MissingFailedPart);
        var identity = new PartitionControlCommandIdentity(CommandOutcomeScopeKind.Partition, seed.Partition,
            PartitionMovementPublicParentRf3Administrator.PrincipalId, request.CommandId);
        var sourceCuts = cuts.Where(value => value.Header is not null).ToArray();
        await Assert.That(sourceCuts.Length).IsEqualTo(seed.Directory.ControlOwner.VoterIds.Length);
        var target = seed.Directory.Owners.Single(entry => entry.Owner.PhysicalShardId
            == seed.FirstRequest.DestinationPhysicalShardId).Owner;
        foreach (var cut in sourceCuts)
        {
            var record = PartitionMovementCapturePointerRf3Fault.Read<PartitionControlCommandRecord>(cut,
                PartitionControlCommandKeys.Authority(identity));
            PartitionControlCommandValidation.Require(record, identity);
            await Assert.That(record.Phase).IsEqualTo(PartitionControlCommandPhase.Finalized);
            await Assert.That(record.OriginalResult!.Error).IsEqualTo((ErrorCode?)ErrorCode.Validation);
            await Assert.That(record.BlobAuthority).IsNull();
            await Assert.That(record.TargetEffect).IsNotNull();
            await Assert.That(record.TargetEffect!.Mutations.IsEmpty).IsTrue();
            await Assert.That(record.TargetEffect.Token.Incarnation).IsEqualTo(target.Incarnation);
            await Assert.That(record.OriginalOutcome).IsNotNull();
            var outcome = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(cut,
                PartitionControlCommandKeys.Original(identity));
            await Assert.That(outcome.Result.Error).IsEqualTo((ErrorCode?)ErrorCode.Validation);
            await Assert.That(outcome.BlobAuthority).IsNull();
            await Assert.That(outcome.PolicyEpoch).IsEqualTo(
                PartitionMovementPublicParentRf3Administrator.InitialDefinition().PolicyEpoch);
            await Assert.That(outcome.Incarnation).IsEqualTo(seed.Directory.ControlOwner.Incarnation);
        }
    }

    private const string MissingFailedPart = "The real post-retirement failed Blob effect is absent.";
}
