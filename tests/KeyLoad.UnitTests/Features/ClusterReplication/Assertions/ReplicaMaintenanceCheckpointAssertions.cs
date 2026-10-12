using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaMaintenanceCheckpointAssertions
{
    internal static async Task RequirePreparedAsync(RequestCqrsCohortRuntimeFixture fixture,
        RequestCqrsCohortCheckpointScenario scenario)
    {
        string[] voters = [RequestCqrsCohortScenario.LocalVoter, RequestCqrsCohortScenario.FirstRemote,
            RequestCqrsCohortScenario.SecondRemote];
        await Assert.That(scenario.Prepared.Count).IsEqualTo(voters.Length);
        var original = scenario.Prepared[voters[0]];
        foreach (var voter in voters)
        {
            var actual = scenario.Prepared[voter];
            await Assert.That(actual.Snapshot.Incarnation).IsEqualTo(fixture.Incarnation);
            await Assert.That(actual.Snapshot.Index).IsEqualTo(1L);
            await Assert.That(actual.Snapshot.Term).IsEqualTo(1L);
            await Assert.That(actual.Snapshot.Sha256).IsEqualTo(original.Snapshot.Sha256);
            await Assert.That(actual.Entries.AsSpan().SequenceEqual(original.Entries)).IsTrue();
        }
    }

    internal static async Task RequirePendingAsync(ReplicaConsensus consensus, CancellationToken token)
    {
        var ordinary = await consensus.StateAsync(token);
        await Assert.That(ordinary.HasCheckpointPrefix).IsNull();
        await Assert.That(ordinary.CheckpointPending).IsNull();
        var deep = await consensus.ReadCheckpointStateAsync(token);
        await Assert.That(deep.HasCheckpointPrefix is true).IsTrue();
        await Assert.That(deep.CheckpointPending is true).IsTrue();
        await Assert.That(deep.SnapshotIndex).IsEqualTo(1L);
    }

    internal static async Task RequireFaultFenceAsync(ReplicaConsensus consensus, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            { _ = await consensus.ReadCheckpointStateAsync(token); }
            catch (global::KeyLoad.KeyLoadException failure) when (failure.Code == global::KeyLoad.ErrorCode.RecoveryRequired)
            { break; }
            await Task.Yield();
        }
        try
        {
            _ = await consensus.StateAsync(token);
            throw new InvalidOperationException("Ordinary state escaped the genuine checkpoint failure fence.");
        }
        catch (global::KeyLoad.KeyLoadException failure) when (failure.Code == global::KeyLoad.ErrorCode.RecoveryRequired) { }
    }

    internal static async Task RequireNativeFailuresAsync(IReadOnlyList<Exception> failures)
    {
        await Assert.That(failures.Count).IsGreaterThan(0);
        foreach (var failure in failures)
        { await RequireNativeFailureAsync(failure); }
    }

    private static async Task RequireNativeFailureAsync(Exception failure)
    {
        if (failure is AggregateException aggregate)
        {
            await RequireNativeFailuresAsync(aggregate.InnerExceptions);
            return;
        }
        await Assert.That(failure.GetType()).IsEqualTo(typeof(IOException));
        await Assert.That(failure.Message).IsEqualTo(RequestCqrsCohortCheckpointScenario.NativeFailure);
    }
}
