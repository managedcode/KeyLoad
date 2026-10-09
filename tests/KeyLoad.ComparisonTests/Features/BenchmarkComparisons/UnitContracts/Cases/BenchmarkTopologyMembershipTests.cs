using System.Collections.Immutable;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-004: fixed benchmark membership is part of the real node-owned replica WAL.</summary>
internal sealed class BenchmarkTopologyMembershipTests
{
    private const int CurrentMembershipVersion = 2;

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task AcIso004FreshBenchmarkPersistsMembershipWithHardStateInOneCommit(int nodes)
    {
        using var fixture = new BenchmarkTopologyMembershipFixture();
        var configuration = fixture.Configuration(nodes);
        byte[] membership;
        using (var store = fixture.Open())
        {
            using var log = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(configuration));
            await Assert.That(store.Position).IsEqualTo(1L);
            await Assert.That(BenchmarkTopologyMembershipFixture.HardState(store)).IsNotNull();
            membership = BenchmarkTopologyMembershipFixture.Membership(store)!;
            await Assert.That(membership).IsNotNull();
            var record = ReplicaProtocolCodec.Deserialize<ReplicaBenchmarkMembershipRecord>(membership);
            await Assert.That(record.Version).IsEqualTo(CurrentMembershipVersion);
            await Assert.That(record.Incarnation).IsEqualTo(fixture.Incarnation);
            await Assert.That(record.VoterIds.ToArray())
                .IsEquivalentTo(configuration.VoterIds.ToArray(), CollectionOrdering.Matching);
            log.SaveTermAndVote(1, configuration.LocalId);
        }
        using var reopened = fixture.Open();
        using var recovered = new DurableReplicaLog(reopened, UnitExecutionOptions.ReplicaConfiguration(configuration));
        await Assert.That(recovered.State.Term).IsEqualTo(1L);
        await Assert.That(recovered.State.VotedFor).IsEqualTo(configuration.LocalId);
        await Assert.That(BenchmarkTopologyMembershipFixture.Membership(reopened)).IsEquivalentTo(membership, CollectionOrdering.Matching);
        await Assert.That(reopened.Position).IsEqualTo(2L);
    }

    [Test]
    public async Task AcIso004FlushedInitialWalReplaysBothAuthorityRecordsAfterInterruption()
    {
        using var fixture = new BenchmarkTopologyMembershipFixture();
        var configuration = fixture.Configuration(3);
        using (var store = fixture.Open((stage, _, _) =>
        {
            if (stage == CommitStage.JournalFlushed)
            {
                throw new InvalidOperationException(BenchmarkTopologyMembershipFixture.PrivateCanary);
            }
        }))
        {
            var failure = BenchmarkTopologyMembershipFixture.Reject(store, configuration);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
            await Assert.That(failure.Message).DoesNotContain(BenchmarkTopologyMembershipFixture.PrivateCanary);
        }
        using var reopened = fixture.Open();
        using var log = new DurableReplicaLog(reopened, UnitExecutionOptions.ReplicaConfiguration(configuration));
        await Assert.That(reopened.Position).IsEqualTo(1L);
        await Assert.That(BenchmarkTopologyMembershipFixture.Membership(reopened)).IsNotNull();
        await Assert.That(BenchmarkTopologyMembershipFixture.HardState(reopened)).IsNotNull();
        await Assert.That(log.State.Term).IsEqualTo(0L);
    }

    [Test]
    public async Task AcIso004PersistedGuardAlsoFencesMembershipAfterOptOut()
    {
        using var fixture = new BenchmarkTopologyMembershipFixture();
        var original = fixture.Configuration(3);
        using (var store = fixture.Open())
        {
            using var log = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(original));
        }
        using var reopened = fixture.Open();
        using var same = new DurableReplicaLog(reopened, UnitExecutionOptions.ReplicaConfiguration(original with { BenchmarkTopology = false }));
        var position = reopened.Position;
        var reordered = original with { VoterIds = original.VoterIds.Reverse().ToImmutableArray() };
        var reduced = fixture.Configuration(1);
        var enlargedProduction = fixture.Configuration(5, benchmark: false);
        foreach (var changed in new[] { reordered, reduced, enlargedProduction })
        {
            var failure = BenchmarkTopologyMembershipFixture.Reject(reopened, changed);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(failure.Message).DoesNotContain(original.LocalId);
            await Assert.That(reopened.Position).IsEqualTo(position);
        }
    }
}
