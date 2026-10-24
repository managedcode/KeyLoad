using KeyLoad.UnitTests.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.CodeQuality.Cases;

internal sealed class SourceManifestChildSettlementTests
{
    [Test]
    public async Task AcCq044CanceledChildJoinsActualExitAndReadersBeforeDisposalAndRootDeletion()
    {
        var joined = await SourceManifestChildSettlement.RunAsync(TestContext.Current!.Execution.CancellationToken);

        await Assert.That(joined.Ready).IsEqualTo(SourceManifestChildSettlement.ReadyMarker);
        await Assert.That(joined.StandardOutput).IsEqualTo(
            SourceManifestChildSettlement.ReadyMarker + Environment.NewLine);
        await Assert.That(joined.StandardError).IsEqualTo(
            SourceManifestChildSettlement.ErrorMarker + Environment.NewLine);
        await Assert.That(joined.PendingBeforeCancellation).IsTrue();
        await Assert.That(joined.Caller.IsCancellationRequested).IsTrue();
        await Assert.That(joined.Original.IsCompletedSuccessfully).IsTrue();
        await Assert.That(joined.Exit.IsCompletedSuccessfully).IsTrue();
        await Assert.That(joined.Output.IsCompletedSuccessfully).IsTrue();
        await Assert.That(joined.Error.IsCompletedSuccessfully).IsTrue();
        await Assert.That(joined.ExitedBeforeDisposal).IsTrue();
        await Assert.That(joined.ProcessDisposed).IsTrue();
        await Assert.That(joined.OwnedRootDeleted).IsTrue();
        await Assert.That(joined.Failures.Count).IsEqualTo(1);
        var cancellation = joined.Failures.Single() as TaskCanceledException;
        await Assert.That(cancellation).IsNotNull();
        await Assert.That(cancellation!.CancellationToken).IsEqualTo(joined.Caller);
        var projected = Assert.ThrowsExactly<TaskCanceledException>(
            () => global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny([.. joined.Failures]));
        await Assert.That(projected).IsSameReferenceAs(cancellation);
    }
}
