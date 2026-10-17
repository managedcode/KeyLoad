namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-004: complements real-node recovery with terminal cleanup of a genuine filesystem task failure.</summary>
internal sealed class ReplicaMaterializerShutdownTests
{
    private const int ImmediateWaitMilliseconds = 0;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static CancellationToken TestToken => TestContext.Current!.Execution.CancellationToken;

    /// <summary>The helper drains actual file work, preserves its original error and closes only its owned synchronization resources.</summary>
    [Test]
    public async Task FilesystemFailurePreservesOriginalErrorAfterPendingWorkAndTerminalCleanup()
    {
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestToken);
        await using var fixture = new ReplicaMaterializerShutdownFixture(linked.Token);
        await fixture.RunAsync(async () =>
        {
            await fixture.Entered.WaitAsync(linked.Token);
            await Assert.That(File.Exists(fixture.MissingFilePath)).IsFalse();
            var terminal = fixture.BeginShutdown();
            try
            {
                await Assert.That(terminal.IsCompleted).IsFalse();
                await Assert.That(fixture.Worker.IsCompleted).IsFalse();
                await Assert.That(fixture.Lifetime.IsCancellationRequested).IsTrue();
                await Assert.That(fixture.Writer.TryWrite(true)).IsFalse();
                await Assert.That(await fixture.Writer.WaitToWriteAsync(linked.Token)).IsFalse();
                await Assert.That(fixture.TerminalPublished.IsCompleted).IsFalse();
            }
            finally { fixture.Release(); }
            var failure = await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => terminal.WaitAsync(linked.Token));
            await Assert.That(fixture.FilesystemFailure).IsNotNull();
            await Assert.That(failure).IsSameReferenceAs(fixture.FilesystemFailure);
            await Assert.That(terminal.IsFaulted).IsTrue();
            await Assert.That(await fixture.TerminalPublished.WaitAsync(linked.Token)).IsTrue();
            await Assert.That(Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.ApplyGate.Wait(ImmediateWaitMilliseconds))).IsNotNull();
            await Assert.That(Assert.ThrowsExactly<ObjectDisposedException>(() => _ = fixture.Lifetime.Token)).IsNotNull();
        });
    }
}
