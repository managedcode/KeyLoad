using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-051: native checkpoint IO precedes metadata publication through the node's planning gate.</summary>
internal sealed class ReplicaCheckpointProtocolGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PublicationObservation = TimeSpan.FromSeconds(2);

    /// <summary>A captured native image cannot compact a suffix while its protocol planning scope remains open.</summary>
    [Test]
    public Task CreateWaitsForSharedPlanningGateAfterNativeImageFlush() => RunAsync(incoming: false);

    /// <summary>A genuinely transferred and installed image obeys the same publication and tail contract.</summary>
    [Test]
    public Task IncomingCompleteWaitsForSharedPlanningGateAfterNativeImageFlush() => RunAsync(incoming: true);

    private static async Task RunAsync(bool incoming)
    {
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        await using var fixture = new ReplicaCheckpointProtocolGateFixture();
        await fixture.RunAsync(async () =>
        {
            await fixture.PrepareAsync(incoming, linked.Token);
            var log = fixture.Node.Log;
            await Assert.That(fixture.Node.Materializer.ProtocolGate).IsSameReferenceAs(log.ProtocolGate);
            fixture.StartPlanning(linked.Token);
            var planning = await fixture.WaitForPlanningAsync(linked.Token);
            var before = log.State;
            await AssertPlanningAsync(planning, before, fixture.Node.Configuration);
            try
            {
                fixture.StartPublication(incoming, linked.Token);
                await fixture.NativeFlushed.WaitAsync(linked.Token);
                fixture.ReleaseNativeFlush();
                var path = await fixture.WaitForPublishedImageAsync(linked.Token);
                await Assert.That(fixture.Node.Canonical.VerifySnapshot(path).AppliedPosition).IsEqualTo(ReplicaCheckpointProtocolGateNode.SnapshotCut);
                await Task.Delay(PublicationObservation, TimeProvider.System, linked.Token);
                await Assert.That(fixture.Publication.IsCompleted).IsFalse();
                await Assert.That(log.State).IsEqualTo(planning.State);
                await Assert.That(log.TermAt(1)).IsEqualTo(ReplicaCheckpointProtocolGateNode.Term);
                await Assert.That(log.Read(2, fixture.Node.Configuration.MaxAppendEntries, fixture.Node.Configuration.MaxAppendBytes).Length).IsEqualTo(3);
            }
            finally
            {
                fixture.ReleaseNativeFlush();
                fixture.ReleasePlanning();
            }
            var joinedPlanning = await fixture.JoinPlanningAsync();
            await AssertPlanningAsync(joinedPlanning, before, fixture.Node.Configuration);
            var snapshot = await fixture.Publication.WaitAsync(linked.Token);
            await fixture.AssertPublishedAsync(snapshot!, linked.Token);
        });
    }

    private static async Task AssertPlanningAsync(ReplicaCheckpointProtocolPlanningObservation planning,
        ReplicaHardState expectedState, ReplicaConfiguration configuration)
    {
        await Assert.That(planning.State).IsEqualTo(expectedState);
        await Assert.That(planning.Term).IsEqualTo(ReplicaCheckpointProtocolGateNode.Term);
        await Assert.That(planning.Suffix.Length).IsEqualTo(3);
        await Assert.That(planning.Suffix[0]).IsEqualTo(new ReplicaEntry(2, ReplicaCheckpointProtocolGateNode.Term, null));
        await Assert.That(planning.Suffix[1]).IsEqualTo(new ReplicaEntry(ReplicaCheckpointProtocolGateNode.SnapshotCut,
            ReplicaCheckpointProtocolGateNode.Term, null));
        await Assert.That(planning.Suffix[^1]).IsEqualTo(new ReplicaEntry(ReplicaCheckpointProtocolGateNode.TailIndex,
            ReplicaCheckpointProtocolGateNode.Term, null));
        await Assert.That(expectedState.VotedFor).IsEqualTo(configuration.LocalId);
    }
}
