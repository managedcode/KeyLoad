using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaReceiverBarrierDeadlineAssertions
{
    internal static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(250);

    internal static async Task SettingsAsync(RequestCqrsCohortRuntimeFixture fixture)
    {
        foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
        {
            var journal = fixture.Node(voter).Journal;
            await Assert.That(journal.ExecutionOptions.Value.CommandTimeout).IsEqualTo(P.ReceiverCommandTimeout);
            await Assert.That(journal.ExecutionOptions.Value.ReadBarrierTimeout).IsEqualTo(TimeSpan.FromSeconds(10));
            await Assert.That(journal.Configuration.RpcTimeout).IsEqualTo(P.RpcTimeout);
            await Assert.That(journal.Configuration.VoterIds.Length).IsEqualTo(3);
            await Assert.That(journal.Configuration.Majority).IsEqualTo(2);
            await Assert.That(journal.Configuration.LowerElectionTimeout).IsEqualTo(P.LowerElectionTimeout);
            await Assert.That(journal.Configuration.UpperElectionTimeout).IsEqualTo(P.UpperElectionTimeout);
            await Assert.That(ReferenceEquals(journal.Log.CanonicalDatabase, journal.Database)).IsTrue();
        }
    }
}
