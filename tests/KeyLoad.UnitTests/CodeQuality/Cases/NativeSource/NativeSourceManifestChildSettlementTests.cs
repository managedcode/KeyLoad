using KeyLoad.UnitTests.Features.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.Features.CodeQuality.Cases;

internal sealed class NativeSourceManifestChildSettlementTests
{
    [Test]
    public async Task AcCq044CanceledNativeChildJoinsActualExitAndReadersBeforeDisposalAndRootDeletion()
    {
        var joined = await NativeSourceManifestChildSettlement.RunAsync(TestContext.Current!.Execution.CancellationToken);

        await Assert.That(joined.Ready).IsEqualTo(NativeSourceManifestChildSettlement.ReadyMarker);
        await Assert.That(joined.StandardOutput).IsEqualTo(
            NativeSourceManifestChildSettlement.ReadyMarker + Environment.NewLine);
        await Assert.That(joined.StandardError).IsEqualTo(
            NativeSourceManifestChildSettlement.ErrorMarker + Environment.NewLine);
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
            () => NativeCoverageImageNodeSettlement.ThrowFailures([.. joined.Failures]));
        await Assert.That(projected).IsSameReferenceAs(cancellation);
    }
}
