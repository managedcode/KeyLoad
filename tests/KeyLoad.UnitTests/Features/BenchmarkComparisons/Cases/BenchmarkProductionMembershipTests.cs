using KeyLoad.Replication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-004: ordinary fixed voter groups persist and require their exact native authority guard.</summary>
internal sealed class BenchmarkProductionMembershipTests
{
    private const int ProductionVoterCount = 5;
    private const long FirstStorePosition = 1;
    private const long VotedStorePosition = 2;
    private const long RepairedStorePositionStep = 1;
    private const long InitialTerm = 1;

    [Test]
    public async Task AcIso004FreshProductionPersistsMembershipAndTermVoteAcrossReopen()
    {
        using var fixture = new BenchmarkTopologyMembershipFixture();
        var configuration = fixture.Configuration(ProductionVoterCount, benchmark: false);
        byte[] membership;
        byte[] hardState;
        using (var store = fixture.Open())
        {
            using var log = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(configuration));
            await Assert.That(store.Position).IsEqualTo(FirstStorePosition);
            membership = BenchmarkTopologyMembershipFixture.Membership(store)!;
            await Assert.That(membership).IsNotNull();
            await Assert.That(BenchmarkTopologyMembershipFixture.HardState(store)).IsNotNull();
            var record = ReplicaProtocolCodec.Deserialize<ReplicaBenchmarkMembershipRecord>(membership);
            await Assert.That(record.Version).IsEqualTo(ReplicaProtocol.FormatVersion);
            await Assert.That(record.Incarnation).IsEqualTo(fixture.Incarnation);
            await Assert.That(record.VoterIds.ToArray())
                .IsEquivalentTo(configuration.VoterIds.ToArray(), CollectionOrdering.Matching);
            log.SaveTermAndVote(InitialTerm, configuration.LocalId);
            hardState = BenchmarkTopologyMembershipFixture.HardState(store)!;
            await Assert.That(store.Position).IsEqualTo(VotedStorePosition);
        }

        using var reopened = fixture.Open();
        using var recovered = new DurableReplicaLog(reopened, UnitExecutionOptions.ReplicaConfiguration(configuration));
        await Assert.That(recovered.State.Term).IsEqualTo(InitialTerm);
        await Assert.That(recovered.State.VotedFor).IsEqualTo(configuration.LocalId);
        await Assert.That(BenchmarkTopologyMembershipFixture.Membership(reopened))
            .IsEquivalentTo(membership, CollectionOrdering.Matching);
        await Assert.That(BenchmarkTopologyMembershipFixture.HardState(reopened))
            .IsEquivalentTo(hardState, CollectionOrdering.Matching);
        await Assert.That(reopened.Position).IsEqualTo(VotedStorePosition);
    }

    [Test]
    public async Task AcIso004MissingProductionMembershipRejectsWithoutMutationAndHealthyFollowup()
    {
        using var fixture = new BenchmarkTopologyMembershipFixture();
        var configuration = fixture.Configuration(ProductionVoterCount, benchmark: false);
        byte[] originalMembership;
        using var store = fixture.Open();
        using (var initial = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(configuration)))
        {
            initial.SaveTermAndVote(InitialTerm, configuration.LocalId);
            originalMembership = BenchmarkTopologyMembershipFixture.Membership(store)!;
        }

        store.Commit((transaction, _) => { transaction.Delete(BenchmarkTopologyMembershipFixture.MembershipKey); return true; });
        var missingPosition = store.Position;
        var missingHardState = BenchmarkTopologyMembershipFixture.HardState(store)!;
        var identity = store.Identity;
        var failure = BenchmarkTopologyMembershipFixture.Reject(store, configuration);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(store.Position).IsEqualTo(missingPosition);
        await Assert.That(store.Identity).IsEqualTo(identity);
        await Assert.That(BenchmarkTopologyMembershipFixture.Membership(store)).IsNull();
        await Assert.That(BenchmarkTopologyMembershipFixture.HardState(store))
            .IsEquivalentTo(missingHardState, CollectionOrdering.Matching);

        store.Commit((transaction, _) => { transaction.Put(BenchmarkTopologyMembershipFixture.MembershipKey, originalMembership); return true; });
        using var healthyFollowup = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(configuration));
        await Assert.That(healthyFollowup.State.Term).IsEqualTo(InitialTerm);
        await Assert.That(healthyFollowup.State.VotedFor).IsEqualTo(configuration.LocalId);
        await Assert.That(BenchmarkTopologyMembershipFixture.Membership(store))
            .IsEquivalentTo(originalMembership, CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(missingPosition + RepairedStorePositionStep);
    }
}
