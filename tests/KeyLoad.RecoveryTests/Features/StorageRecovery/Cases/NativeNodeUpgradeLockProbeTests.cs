using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NativeNodeUpgradeLockProbeTests
{
    [Test]
    public async Task NativeProbeRejectsARealOwnerAndSucceedsAfterItsRelease()
    {
        await NodeEpochCoordinatorRejectionTests.WithNodeAsync(
            async (_, source, _, _, sourceInventory, cancellationToken) =>
            {
                using (ServerNodeUpgradeLocks.Acquire(source, RecoveryExecutionOptions.NodeUpgrade()))
                {
                    var rejected = Assert.ThrowsExactly<IOException>(
                        () => NativeNodeUpgradeLockProbe.Verify(source, NativeNodeUpgradeLockBoundary.NegativeControl));
                    await Assert.That(rejected.Data[NativeNodeUpgradeLockProbe.BoundaryDataKey])
                        .IsEqualTo(NativeNodeUpgradeLockBoundary.NegativeControl);
                }

                NativeNodeUpgradeLockProbe.Verify(source, NativeNodeUpgradeLockBoundary.NegativeControl);
                await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, cancellationToken);
            }, TestContext.Current!.Execution.CancellationToken);
    }
}
