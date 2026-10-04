using KeyLoad.CrashHost;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NodeEpochRegularFileFifoTests
{
    private const string FifoName = "unsupported-input.fifo";

    [Test]
    public async Task AcEpoch012RejectsFifoInOrdinaryNodeInventoryWithoutSourceMutation()
    {
        await NodeEpochCoordinatorRejectionTests.WithNodeAsync(async (root, source, destination, profile,
            original, token) =>
        {
            var fifo = Path.Combine(source, "database", FifoName);
            await NodeEpochRegularFileProcess.CreateFifoAsync(fifo, token);
            try
            {
                await NodeEpochRegularFileProcess.AssertRejectedAsync(source, destination, profile, fifo, token);
            }
            finally { File.Delete(fifo); }
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, original, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch012RejectsFifoAtRequiredNodeOwnerLockWithoutSourceMutation()
    {
        await NodeEpochCoordinatorRejectionTests.WithNodeAsync(async (root, source, destination, profile,
            original, token) =>
        {
            var lockPath = Path.Combine(source, ServerNodeUpgradeProtocol.NodeOwner);
            var retained = Path.Combine(root, "retained-node-owner.bin");
            await AssertFifoReplacementRejectedAsync(source, destination, profile, lockPath, retained,
                original, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch012RejectsFifoAtOldSnapshotSourceWithoutSourceMutation()
    {
        await NodeEpochCoordinatorRejectionTests.WithNodeAsync(async (root, source, destination, profile,
            original, token) =>
        {
            var snapshots = Path.Combine(source, ServerNodeUpgradeProtocol.Snapshots);
            var snapshot = Directory.EnumerateFiles(snapshots, "*.snapshot").Order(StringComparer.Ordinal).First();
            var retained = Path.Combine(root, "retained-native5-snapshot.bin");
            var imageOutput = Path.Combine(root, "converted-image.snapshot");
            File.Move(snapshot, retained);
            await NodeEpochRegularFileProcess.CreateFifoAsync(snapshot, token);
            try
            {
                await NodeEpochRegularFileProcess.AssertRejectedAsync(source, destination, profile, snapshot, token);
                await NodeEpochRegularFileProcess.AssertImageRejectedAsync(snapshot, imageOutput, profile, token);
            }
            finally
            {
                File.Delete(snapshot);
                File.Move(retained, snapshot);
            }
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, original, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    private static async Task AssertFifoReplacementRejectedAsync(string source, string destination,
        EpochPriorNodeProfile profile, string originalPath, string retainedPath, NodeEpochInventory original,
        CancellationToken cancellationToken)
    {
        File.Move(originalPath, retainedPath);
        await NodeEpochRegularFileProcess.CreateFifoAsync(originalPath, cancellationToken);
        try
        {
            await NodeEpochRegularFileProcess.AssertRejectedAsync(source, destination, profile, originalPath,
                cancellationToken);
        }
        finally
        {
            File.Delete(originalPath);
            File.Move(retainedPath, originalPath);
        }
        await NodeEpochInventoryCapture.AssertUnchangedAsync(source, original, cancellationToken);
    }
}
