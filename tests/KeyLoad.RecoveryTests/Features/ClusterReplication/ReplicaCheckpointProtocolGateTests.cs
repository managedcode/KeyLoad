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
            await log.ProtocolGate.WaitAsync(linked.Token);
            try
            {
                var before = log.State;
                fixture.StartPublication(incoming, linked.Token);
                await fixture.NativeFlushed.WaitAsync(linked.Token);
                fixture.ReleaseNativeFlush();
                var path = await fixture.WaitForPublishedImageAsync(linked.Token);
                await Assert.That(fixture.Node.Canonical.VerifySnapshot(path).AppliedPosition).IsEqualTo(ReplicaCheckpointProtocolGateNode.SnapshotCut);
                await Task.Delay(PublicationObservation, TimeProvider.System, linked.Token);
                await Assert.That(fixture.Publication.IsCompleted).IsFalse();
                await Assert.That(log.State).IsEqualTo(before);
                await Assert.That(log.TermAt(1)).IsEqualTo(ReplicaCheckpointProtocolGateNode.Term);
                await Assert.That(log.Read(2, fixture.Node.Configuration.MaxAppendEntries, fixture.Node.Configuration.MaxAppendBytes).Length).IsEqualTo(3);
            }
            finally
            {
                fixture.ReleaseNativeFlush();
                log.ProtocolGate.Release();
            }
            var snapshot = await fixture.Publication.WaitAsync(linked.Token);
            await fixture.AssertPublishedAsync(snapshot!, linked.Token);
        });
    }
}
