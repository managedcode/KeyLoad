using System.Collections.Immutable;
using System.Text;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalShardCatalogVoterIdentityTests
{
    private const int ExactVoterIdBytes = 512;
    private const int OverlongVoterIdBytes = 513;
    private static readonly Guid ShardId = Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");
    private static readonly Guid Incarnation = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    [Test]
    public async Task AcScat002PersistsNonAsciiVoterAtExactUtf8ByteBound()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var nonAscii = new string('v', 510) + "é";
        await Assert.That(Encoding.UTF8.GetByteCount(nonAscii)).IsEqualTo(ExactVoterIdBytes);
        var voters = ImmutableArray.Create(nonAscii, PhysicalShardCatalogVoterIds.Second,
            PhysicalShardCatalogVoterIds.Third);
        var request = PhysicalShardCatalogBootstrapTests.Request(ShardId, Incarnation, voters);

        await Assert.That(fixture.Bootstrap(request).Error).IsNull();
        var actual = fixture.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogFixture.RootPrincipalId);
        await Assert.That(actual.DefaultShard.VoterIds.SequenceEqual(voters, StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task AcScat002RejectsOverlongNullWhitespaceAndExactDuplicateNativeRequests()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var overlong = new string('v', 511) + "é";
        await Assert.That(Encoding.UTF8.GetByteCount(overlong)).IsEqualTo(OverlongVoterIdBytes);
        var invalidLists = new[]
        {
            ImmutableArray.Create(overlong),
            ImmutableArray.Create("  "),
            ImmutableArray.Create(PhysicalShardCatalogVoterIds.First, PhysicalShardCatalogVoterIds.First)
        };

        foreach (var voters in invalidLists)
        {
            var request = PhysicalShardCatalogBootstrapTests.Request(ShardId, Incarnation, voters);
            await Assert.That(fixture.Bootstrap(request, Guid.NewGuid()).Error).IsEqualTo(ErrorCode.Validation);
            var absent = Assert.ThrowsExactly<KeyLoadException>(() =>
                fixture.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogFixture.RootPrincipalId));
            await Assert.That(absent.Code).IsEqualTo(ErrorCode.NotFound);
        }
        var nullVoter = PhysicalShardCatalogBootstrapTests.Request(ShardId, Incarnation,
            ImmutableArray.CreateRange<string>([null!]));
        var malformedNative = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Bootstrap(nullVoter));
        await Assert.That(malformedNative.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(fixture.BootstrapPublic(nullVoter).Error).IsEqualTo(ErrorCode.Validation);
        var missing = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogFixture.RootPrincipalId));
        await Assert.That(missing.Code).IsEqualTo(ErrorCode.NotFound);
    }

    [Test]
    public async Task AcScat002TreatsVoterIdentityAsOrdinalAndPreservesStoredOrder()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var voters = ImmutableArray.Create("http://node1:8080", "HTTP://NODE1:8080", "http://node3:8080");
        var request = PhysicalShardCatalogBootstrapTests.Request(ShardId, Incarnation, voters);
        await Assert.That(fixture.Bootstrap(request).Error).IsNull();
        var stored = fixture.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogFixture.RootPrincipalId);
        await Assert.That(stored.DefaultShard.VoterIds.SequenceEqual(voters, StringComparer.Ordinal)).IsTrue();
        var originalBytes = fixture.ReadCatalogBytes();
        var reordered = request with { VoterIds = [voters[1], voters[0], voters[2]] };

        await Assert.That(fixture.Bootstrap(reordered, Guid.NewGuid()).Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(fixture.ReadCatalogBytes().SequenceEqual(originalBytes)).IsTrue();
        var exactHost = fixture.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogFixture.RootPrincipalId,
            ShardId, Incarnation, voters);
        await Assert.That(exactHost.DefaultShard.VoterIds.SequenceEqual(voters, StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task AcScat002MalformedPersistedVoterListsAreCorruptionWithoutMutation()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        var request = PhysicalShardCatalogBootstrapTests.Request(ShardId, Incarnation,
            PhysicalShardCatalogVoterIds.Standard);
        await Assert.That(fixture.Bootstrap(request).Error).IsNull();
        var catalog = fixture.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogFixture.RootPrincipalId);
        var badLists = new[]
        {
            ImmutableArray.CreateRange<string>([null!]),
            ImmutableArray.Create(" "),
            ImmutableArray.Create(new string('x', OverlongVoterIdBytes)),
            ImmutableArray.Create(PhysicalShardCatalogVoterIds.First, PhysicalShardCatalogVoterIds.First)
        };

        foreach (var voters in badLists)
        {
            fixture.ReplaceCatalog(catalog with
            { DefaultShard = catalog.DefaultShard with { VoterIds = voters } });
            var before = fixture.ReadCatalogBytes();
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                fixture.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogFixture.RootPrincipalId));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(fixture.ReadCatalogBytes().SequenceEqual(before)).IsTrue();
        }
    }
}
