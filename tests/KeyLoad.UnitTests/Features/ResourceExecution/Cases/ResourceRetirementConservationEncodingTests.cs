using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Encoding and algebra regressions only; these do not simulate a successful phase23 or an RF3 receipt.</summary>
public sealed class ResourceRetirementConservationEncodingTests
{
    [Test]
    public async Task ExactAuthorityRowsCommuteWhileChangedBytesAndDuplicateCountRemainVisible()
    {
        var firstKey = KeyCodec.Encode("authority-control", "first");
        var secondKey = KeyCodec.Encode("authority-control", "second");
        var firstValue = new byte[] { 3, 1, 4 };
        var secondValue = new byte[] { 1, 5, 9 };
        var forward = new ResourceRetirementAuthorityAccumulator();
        forward.Add(firstKey, firstValue);
        forward.Add(secondKey, secondValue);
        var reverse = new ResourceRetirementAuthorityAccumulator();
        reverse.Add(secondKey, secondValue);
        reverse.Add(firstKey, firstValue);
        await Assert.That(forward.Complete().Same(reverse.Complete())).IsTrue();
        var changed = new ResourceRetirementAuthorityAccumulator();
        changed.Add(firstKey, firstValue);
        changed.Add(secondKey, new byte[] { 1, 5, 8 });
        await Assert.That(forward.Complete().Same(changed.Complete())).IsFalse();
        var duplicate = new ResourceRetirementAuthorityAccumulator();
        duplicate.Add(firstKey, firstValue);
        duplicate.Add(firstKey, firstValue);
        await Assert.That(duplicate.Complete().Digest.Span.SequenceEqual(new byte[32])).IsTrue();
        await Assert.That(duplicate.Complete().Same(new ResourceRetirementAuthorityAccumulator().Complete())).IsFalse();
    }

    [Test]
    public async Task GeneratedStepRoundTripBindsAuthorityAdditionsAndPreservesTheOriginalFloor()
    {
        var owner = new PhysicalShardRecord(Guid.NewGuid(), Guid.NewGuid(),
            ImmutableArray.Create("conservation-a", "conservation-b", "conservation-c"), 1);
        var lifetime = new ResourceLifetimeV1("tenant", "database", "collection", "domain", ResourceKind.Collection,
            new ResourceIncarnationV1(new string('a', 64)), new string('b', 64));
        var before = ResourceRetirementConservation.Initial(owner);
        var authority = new ResourceRetirementAuthorityAccumulator();
        authority.Add(KeyCodec.Encode("typed-encoding-control"), new byte[] { 2, 7, 1 });
        var expectedAuthority = authority.Complete();
        var originalBytes = NativeSerialization.Serialize(before);
        var step = ResourceRetirementConservation.CreateStep(before, lifetime, Guid.NewGuid(), Guid.NewGuid(),
            1, expectedAuthority);
        var restored = NativeSerialization.Deserialize<ResourceRetirementConservationStepV1>(NativeSerialization.Serialize(step));
        var after = ResourceRetirementConservation.Advance(before, restored);
        await Assert.That(after.Revision).IsEqualTo(1L);
        await Assert.That(after.RetiredLifetimeCount).IsEqualTo(1L);
        await Assert.That(new ResourceRetirementAuthoritySummary(after.AuthorityRecordCount, after.AuthorityDigest)
            .Same(expectedAuthority)).IsTrue();
        await Assert.That(after.BaselineOriginKey.IsEmpty).IsTrue();
        var roundTrip = NativeSerialization.Deserialize<ResourceRetirementConservationFloorV1>(NativeSerialization.Serialize(after));
        await Assert.That(ResourceRetirementConservation.Same(after, roundTrip)).IsTrue();
        var changedDigest = restored.AddedAuthorityDigest.ToArray();
        changedDigest[0] ^= 1;
        var invalid = restored with
        {
            AddedAuthorityDigest = changedDigest
        };
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ResourceRetirementConservation.Advance(before, invalid));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(NativeSerialization.Serialize(before).AsSpan().SequenceEqual(originalBytes)).IsTrue();
    }
}
