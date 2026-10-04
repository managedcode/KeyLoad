using KeyLoad.Replication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-IS-002/004 and AC-ISO-004: unsupported authority formats never initialize or rewrite a replica.</summary>
internal sealed class BenchmarkTopologyMembershipVersionTests
{
    private const int CurrentMembershipVersion = 2;
    private const int LegacyMembershipVersion = 1;
    private const int VoterCount = 3;
    private const long RetainedTerm = 1;
    private const long PersistedPosition = 3;

    [Test]
    [Arguments(1, true)]
    [Arguments(1, false)]
    [Arguments(3, true)]
    [Arguments(3, false)]
    public Task AcIs004NativeMembershipVersionsRejectWithoutChangingPersistedAuthority(int version, bool benchmark)
        => RejectWithoutMutationAsync(record => ReplicaProtocolCodec.Serialize(record with { Version = version }), benchmark);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task AcIs004LegacyJsonMembershipRejectsWithoutFallbackOrInitialization(bool benchmark)
        => RejectWithoutMutationAsync(record => JsonDefaults.Serialize(record with { Version = LegacyMembershipVersion }), benchmark);

    private static async Task RejectWithoutMutationAsync(Func<ReplicaBenchmarkMembershipRecord, byte[]> encode, bool benchmark)
    {
        using var fixture = new BenchmarkTopologyMembershipFixture();
        var configuration = fixture.Configuration(VoterCount);
        byte[] replacement;
        byte[] hardState;
        using (var store = fixture.Open())
        {
            using var log = new DurableReplicaLog(store, configuration);
            log.SaveTermAndVote(RetainedTerm, configuration.LocalId);
            var record = ReplicaProtocolCodec.Deserialize<ReplicaBenchmarkMembershipRecord>(BenchmarkTopologyMembershipFixture.Membership(store)!);
            await Assert.That(record.Version).IsEqualTo(CurrentMembershipVersion);
            await Assert.That(record.Incarnation).IsEqualTo(fixture.Incarnation);
            await Assert.That(record.VoterIds.ToArray()).IsEquivalentTo(configuration.VoterIds.ToArray(), CollectionOrdering.Matching);
            hardState = BenchmarkTopologyMembershipFixture.HardState(store)!;
            replacement = encode(record);
            store.Commit((transaction, _) =>
            {
                transaction.Put(BenchmarkTopologyMembershipFixture.MembershipKey, replacement);
                return true;
            });
            await Assert.That(store.Position).IsEqualTo(PersistedPosition);
        }

        using var reopened = fixture.Open();
        var identity = reopened.Identity;
        var failure = BenchmarkTopologyMembershipFixture.Reject(reopened, configuration with { BenchmarkTopology = benchmark });
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(reopened.Position).IsEqualTo(PersistedPosition);
        await Assert.That(reopened.Identity).IsEqualTo(identity);
        await Assert.That(BenchmarkTopologyMembershipFixture.HardState(reopened)).IsEquivalentTo(hardState, CollectionOrdering.Matching);
        await Assert.That(BenchmarkTopologyMembershipFixture.Membership(reopened)).IsEquivalentTo(replacement, CollectionOrdering.Matching);
        var retained = ReplicaProtocolCodec.Deserialize<ReplicaHardState>(BenchmarkTopologyMembershipFixture.HardState(reopened)!);
        await Assert.That(retained.Term).IsEqualTo(RetainedTerm);
        await Assert.That(retained.VotedFor).IsEqualTo(configuration.LocalId);
    }
}
