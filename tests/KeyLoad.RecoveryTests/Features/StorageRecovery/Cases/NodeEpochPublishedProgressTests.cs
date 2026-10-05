using KeyLoad.CrashHost;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NodeEpochPublishedProgressTests
{
    [Test]
    public async Task AcEpoch009RejectsMissingPublishedProgressWithoutMutatingEitherNode()
    {
        await NodeEpochCoordinatorRejectionTests.WithNodeAsync(async (
            _root, source, destination, profile, sourceInventory, token) =>
        {
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);
            _ = ServerNodeFormatUpgrade.Prepare(source, RecoveryServerRuntimeOptions.Runtime(options));
            _ = ServerNodeFormatUpgrade.Publish(source, RecoveryServerRuntimeOptions.Runtime(options));
            File.Delete(ProgressPath(destination));
            var targetInventory = await NodeEpochInventoryCapture.CaptureAsync(destination, token);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.Prepare(source, RecoveryServerRuntimeOptions.Runtime(options)));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.FormatUnsupported);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(destination, targetInventory, token);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch009RejectsCorruptPublishedProgressWithoutMutatingEitherNode()
    {
        await NodeEpochCoordinatorRejectionTests.WithNodeAsync(async (
            _root, source, destination, profile, sourceInventory, token) =>
        {
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);
            _ = ServerNodeFormatUpgrade.Prepare(source, RecoveryServerRuntimeOptions.Runtime(options));
            _ = ServerNodeFormatUpgrade.Publish(source, RecoveryServerRuntimeOptions.Runtime(options));
            await CorruptAsync(ProgressPath(destination), token);
            var targetInventory = await NodeEpochInventoryCapture.CaptureAsync(destination, token);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.Prepare(source, RecoveryServerRuntimeOptions.Runtime(options)));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Corruption);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(destination, targetInventory, token);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    private static string ProgressPath(string destination)
        => Path.Combine(destination, ServerNodeUpgradeProtocol.ProgressReceipt);

    private static async Task CorruptAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        bytes[^1] ^= 0x01;
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
    }
}
