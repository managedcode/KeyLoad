using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalShardCatalogConflictTests
{
    private static readonly Guid ShardId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");
    private static readonly Guid Incarnation = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly ImmutableArray<string> Voters = PhysicalShardCatalogVoterIds.Standard;

    [Test]
    public async Task AcScat001DifferentConfiguredIdentityAndNonzeroExpectedRevisionConflict()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var request = PhysicalShardCatalogBootstrapTests.Request(ShardId, Incarnation, Voters);
        await Assert.That(fixture.Bootstrap(request).Error).IsNull();
        var before = fixture.ReadCatalogBytes();
        var differentShard = request with { PhysicalShardId = Guid.NewGuid() };
        var differentIncarnation = request with { Incarnation = Guid.NewGuid() };
        var reorderedVoters = request with { VoterIds = [Voters[1], Voters[0], Voters[2]] };
        var emptyVoters = request with { VoterIds = ImmutableArray<string>.Empty };
        var duplicateVoters = request with { VoterIds = [Voters[0], Voters[0], Voters[2]] };
        await Assert.That(fixture.Bootstrap(differentShard).Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(fixture.Bootstrap(differentIncarnation).Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(fixture.Bootstrap(reorderedVoters).Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(fixture.Bootstrap(emptyVoters, Guid.NewGuid()).Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(fixture.Bootstrap(duplicateVoters, Guid.NewGuid()).Error).IsEqualTo(ErrorCode.Validation);
        var stale = request with { ExpectedRevision = 1 };
        await Assert.That(fixture.Bootstrap(stale, Guid.NewGuid()).Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(fixture.Bootstrap(request with { ExpectedRevision = -1 }, Guid.NewGuid()).Error)
            .IsEqualTo(ErrorCode.Conflict);
        var stored = fixture.Store.Read(view => view.GetRecord<PhysicalShardCatalog>(PhysicalShardCatalogRecordSerialization.CatalogKey()));
        await Assert.That(stored!.DefaultShard.PhysicalShardId).IsEqualTo(ShardId);
        await Assert.That(fixture.ReadCatalogBytes().SequenceEqual(before)).IsTrue();
        await Assert.That(stored.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task AcScat001ChangedBodyUnderSameCommandIdentityIsConflict()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var commandId = PhysicalShardCatalogIdentity.CreateBootstrapCommandId(ShardId);
        var original = PhysicalShardCatalogBootstrapTests.Request(ShardId, Incarnation, Voters);
        await Assert.That(fixture.Bootstrap(original, commandId).Error).IsNull();
        var changed = original with { Incarnation = Guid.NewGuid() };
        await Assert.That(fixture.Bootstrap(changed, commandId).Error).IsEqualTo(ErrorCode.Conflict);
        var stored = fixture.Store.Read(view => view.GetRecord<PhysicalShardCatalog>(PhysicalShardCatalogRecordSerialization.CatalogKey()));
        await Assert.That(stored!.DefaultShard.Incarnation).IsEqualTo(Incarnation);
    }
}
