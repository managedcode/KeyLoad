using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalShardCatalogValidationTests
{
    private const int MaximumCatalogBytes = 8192;
    private const int OversizedCatalogBytes = 8193;
    private static readonly Guid ShardId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");
    private static readonly Guid Incarnation = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly ImmutableArray<string> Voters = [PhysicalShardCatalogVoterIds.First];

    [Test]
    public async Task AcScat002RejectsInvalidIdentityVersionVotersAndOversizedRequest()
    {
        AssertValidation(null);
        AssertValidation(new(2, 0, ShardId, Incarnation, Voters));
        AssertValidation(new(1, 0, Guid.Empty, Incarnation, Voters));
        AssertValidation(new(1, 0, ShardId, Guid.Empty, Voters));
        AssertValidation(new(1, 0, ShardId, Incarnation, default));
        AssertValidation(new(1, 0, ShardId, Incarnation, ImmutableArray<string>.Empty));
        AssertValidation(new(1, 0, ShardId, Incarnation, ImmutableArray.CreateRange<string>([null!])));
        AssertValidation(new(1, 0, ShardId, Incarnation, ["\t "]));
        AssertValidation(new(1, 0, ShardId, Incarnation, [Voters[0], Voters[0]]));
        var tooMany = ImmutableArray.Create(
            "http://node1:8080", "http://node2:8080", "http://node3:8080", "http://node4:8080");
        AssertValidation(new(1, 0, ShardId, Incarnation, tooMany));
        AssertValidation(new(1, 0, ShardId, Incarnation, [new string('x', 513)]));
        PhysicalShardCatalogValidation.ValidateEncodedRequestLength(MaximumCatalogBytes);
        var sizeFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            PhysicalShardCatalogValidation.ValidateEncodedRequestLength(OversizedCatalogBytes));
        await Assert.That(sizeFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcScat002RejectsMalformedCommittedCatalogAsCorruption()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var malformed = new PhysicalShardCatalog(2, 1,
            new PhysicalShardRecord(ShardId, Incarnation, Voters, 1));
        await AssertCorruptReadPreservesBytesAsync(fixture, malformed);
    }

    [Test]
    public async Task AcScat002RevisionAndEpochCorruptionFailReadWithoutMutatingTheRow()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var request = PhysicalShardCatalogBootstrapTests.Request(ShardId, Incarnation, Voters);
        await Assert.That(fixture.Bootstrap(request).Error).IsNull();
        var initial = fixture.Store.Read(view => view.GetRecord<PhysicalShardCatalog>(
            PhysicalShardCatalogRecordSerialization.CatalogKey()))!;
        await AssertCorruptReadPreservesBytesAsync(fixture, initial with { Revision = 0 });
        await AssertCorruptReadPreservesBytesAsync(fixture, initial with
        { DefaultShard = initial.DefaultShard with { PlacementEpoch = 0 } });
    }

    [Test]
    public async Task AcScat002OversizedAndEmptyMalformedNativeRowsRemainUnchanged()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var request = PhysicalShardCatalogBootstrapTests.Request(ShardId, Incarnation, Voters);
        await Assert.That(fixture.Bootstrap(request).Error).IsNull();
        await AssertCorruptBytesPreservedAsync(fixture, new byte[OversizedCatalogBytes]);
        await AssertCorruptBytesPreservedAsync(fixture, []);
    }

    [Test]
    public async Task AcScat002UnsupportedNativeEnvelopeVersionIsCorruptionWithoutMutation()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var request = PhysicalShardCatalogBootstrapTests.Request(ShardId, Incarnation, Voters);
        await Assert.That(fixture.Bootstrap(request).Error).IsNull();
        var catalog = fixture.Store.Read(view => view.GetRecord<PhysicalShardCatalog>(
            PhysicalShardCatalogRecordSerialization.CatalogKey()))!;
        var unsupported = PhysicalShardCatalogUnsupportedEnvelopeFixture.Encode(catalog);
        await AssertCorruptBytesPreservedAsync(fixture, unsupported);
    }

    private static async Task AssertCorruptReadPreservesBytesAsync(PhysicalShardCatalogFixture fixture,
        PhysicalShardCatalog catalog)
    {
        fixture.ReplaceCatalog(catalog);
        await AssertCorruptBytesPreservedAsync(fixture, fixture.ReadCatalogBytes());
    }

    private static async Task AssertCorruptBytesPreservedAsync(PhysicalShardCatalogFixture fixture, byte[] bytes)
    {
        fixture.ReplaceCatalogBytes(bytes);
        var before = fixture.ReadCatalogBytes();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogFixture.RootPrincipalId));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(fixture.ReadCatalogBytes().SequenceEqual(before)).IsTrue();
    }

    private static void AssertValidation(BootstrapPhysicalShardCatalogRequest? request)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            PhysicalShardCatalogValidation.ValidateRequest(request));
        if (failure.Code != ErrorCode.Validation)
        {
            throw new InvalidOperationException("The malformed catalog request must be validation failure.");
        }
    }

}
