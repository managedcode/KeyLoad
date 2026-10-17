using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalShardCatalogBootstrapTests
{
    private static readonly Guid ShardId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");
    private static readonly Guid Incarnation = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly ImmutableArray<string> Voters = PhysicalShardCatalogVoterIds.Standard;

    [Test]
    public async Task AcScat001CommitsOneCanonicalNativeRecordAndReopensExactly()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var request = Request(ShardId, Incarnation, Voters);
        var first = fixture.Bootstrap(request);
        await Assert.That(first.Error).IsNull();
        var catalog = ReadCatalog(fixture);
        await Assert.That(catalog.Version).IsEqualTo(1);
        await Assert.That(catalog.Revision).IsEqualTo(1);
        await Assert.That(catalog.DefaultShard.PhysicalShardId).IsEqualTo(ShardId);
        await Assert.That(catalog.DefaultShard.Incarnation).IsEqualTo(Incarnation);
        await Assert.That(catalog.DefaultShard.VoterIds.SequenceEqual(Voters, StringComparer.Ordinal)).IsTrue();
        await Assert.That(catalog.DefaultShard.PlacementEpoch).IsEqualTo(1);
        await Assert.That(Convert.ToHexString(PhysicalShardCatalogRecordSerialization.CatalogKey()))
            .IsEqualTo("0150706879736963616C2D73686172642D636174616C6F6700005076310000");
        var beforeBytes = NativeSerialization.Serialize(catalog);
        fixture.Reopen();
        var after = ReadCatalog(fixture);
        await Assert.That(NativeSerialization.Serialize(after).SequenceEqual(beforeBytes)).IsTrue();
        var principal = fixture.Store.Read(view =>
            view.GetRecord<PrincipalRecord>(KeyLoad.Core.KeySpace.Principal(PhysicalShardCatalogFixture.RootPrincipalId)));
        await Assert.That(principal).IsNotNull();
    }

    [Test]
    public async Task AcScat001AllowsExactDifferentCommandRetryAndSameIdentityReplay()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var request = Request(ShardId, Incarnation, Voters);
        var commandId = PhysicalShardCatalogIdentity.CreateBootstrapCommandId(ShardId);
        var first = fixture.Bootstrap(request, commandId);
        var retry = fixture.Bootstrap(request, commandId);
        var repeated = fixture.Bootstrap(request, Guid.NewGuid());
        await Assert.That(first.Error).IsNull();
        await Assert.That(retry.Error).IsNull();
        await Assert.That(repeated.Error).IsNull();
        await Assert.That(ReadCatalog(fixture).Revision).IsEqualTo(1);
    }

    internal static BootstrapPhysicalShardCatalogRequest Request(Guid shardId, Guid incarnation,
        ImmutableArray<string> voters) => new(1, 0, shardId, incarnation, voters);

    private static PhysicalShardCatalog ReadCatalog(PhysicalShardCatalogFixture fixture)
        => fixture.Store.Read(view => view.GetRecord<PhysicalShardCatalog>(PhysicalShardCatalogRecordSerialization.CatalogKey())!);
}
