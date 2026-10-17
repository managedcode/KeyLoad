using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ResourceRetirementReplicaCountTests
{
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(5)]
    public async Task ConservationBindsTheActualOrderedCopyGroup(int replicas)
    {
        var owner = new PhysicalShardRecord(Guid.NewGuid(), Guid.NewGuid(),
            Enumerable.Range(1, replicas).Select(index => $"replica-{index}").ToImmutableArray(), 1);
        var floor = ResourceRetirementConservation.Initial(owner);
        var step = Create(floor);
        var restored = NativeSerialization.Deserialize<ResourceRetirementConservationStepV1>(NativeSerialization.Serialize(step));
        var after = ResourceRetirementConservation.Advance(floor, restored);
        await Assert.That(after.PhysicalOwner.VoterIds.SequenceEqual(owner.VoterIds)).IsTrue();
        var changed = restored with { PhysicalOwner = owner with { VoterIds = owner.VoterIds.Reverse().ToImmutableArray() } };
        if (replicas > 1)
        {
            await Assert.That(() => ResourceRetirementConservation.Advance(floor, changed)).Throws<KeyLoadException>();
        }
    }

    [Test]
    public async Task ExistingThreeCopyDigestRetainsItsExactEncoding()
    {
        var owner = new PhysicalShardRecord(Guid.NewGuid(), Guid.NewGuid(),
            ImmutableArray.Create("first", "second", "third"), 1);
        var step = Create(ResourceRetirementConservation.Initial(owner));
        var lifetime = step.Lifetime;
        var original = SHA256.HashData(KeyCodec.Encode("keyload.resource-retirement.conservation-step.v1", 1,
            owner.PhysicalShardId, owner.Incarnation, owner.PlacementEpoch, 3,
            owner.VoterIds[0], owner.VoterIds[1], owner.VoterIds[2], step.Revision,
            lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName, lifetime.TransactionDomainId,
            (int)lifetime.Kind, lifetime.Incarnation!.Value, lifetime.CreationFingerprint,
            step.RetirementId, step.ReleaseCommandId, step.AppliedPosition, step.PreviousDigest.ToArray(),
            step.AddedAuthorityRecords, step.AddedAuthorityDigest.ToArray()));
        await Assert.That(step.Digest.Span.SequenceEqual(original)).IsTrue();
    }

    private static ResourceRetirementConservationStepV1 Create(ResourceRetirementConservationFloorV1 floor)
    {
        var lifetime = new ResourceLifetimeV1("tenant", "database", "collection", "domain", ResourceKind.Collection,
            new ResourceIncarnationV1(new string('a', 64)), new string('b', 64));
        return ResourceRetirementConservation.CreateStep(floor, lifetime, Guid.NewGuid(), Guid.NewGuid(), 1,
            new ResourceRetirementAuthorityAccumulator().Complete());
    }
}
