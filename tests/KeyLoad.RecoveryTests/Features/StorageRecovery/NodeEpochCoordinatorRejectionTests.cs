using KeyLoad.CrashHost;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NodeEpochCoordinatorRejectionTests
{
    private const string TrialPrefix = "keyload-node-epoch-reject-";
    private const string SourceName = "prior-node";
    private const string TargetName = "current-node";
    private const int CleanupSeconds = 30;

    [Test]
    public async Task AcEpoch010RejectsForeignNestedStageFileAndPreservesBothInventories()
    {
        await WithNodeAsync(async (root, source, destination, profile, sourceInventory, token) =>
        {
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);
            _ = ServerNodeFormatUpgrade.Prepare(source, options);
            var foreignPath = Path.Combine(destination + ServerNodeUpgradeProtocol.StageSuffix,
                ServerNodeUpgradeProtocol.Canonical, "tree", "foreign.after-boundary");
            byte[] foreignBytes = [0x42, 0x00, 0x7A];
            await File.WriteAllBytesAsync(foreignPath, foreignBytes, token);
            var stage = destination + ServerNodeUpgradeProtocol.StageSuffix;
            var stageInventory = await NodeEpochInventoryCapture.CaptureAsync(stage, token);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.VerifyPrepared(source, options));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Corruption);
            var retry = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.Prepare(source, options));
            await Assert.That(retry.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(Directory.Exists(destination)).IsFalse();
            await NodeEpochInventoryCapture.AssertUnchangedAsync(stage, stageInventory, token);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);
            var retainedBytes = await File.ReadAllBytesAsync(foreignPath, token);
            await Assert.That(retainedBytes.SequenceEqual(foreignBytes)).IsTrue();
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch010RejectsCorruptProgressReceiptAndPreservesOwnedStage()
    {
        await WithNodeAsync(async (root, source, destination, profile, sourceInventory, token) =>
        {
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);
            _ = ServerNodeFormatUpgrade.Prepare(source, options);
            var stage = destination + ServerNodeUpgradeProtocol.StageSuffix;
            File.Delete(Path.Combine(stage, ServerNodeUpgradeProtocol.PreparedReceipt));
            await CorruptReceiptAsync(Path.Combine(stage, ServerNodeUpgradeProtocol.ProgressReceipt), token);
            var stageInventory = await NodeEpochInventoryCapture.CaptureAsync(stage, token);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.Prepare(source, options));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(Directory.Exists(destination)).IsFalse();
            await NodeEpochInventoryCapture.AssertUnchangedAsync(stage, stageInventory, token);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch010RejectsCorruptOwnerReceiptAndPreservesOwnedStage()
    {
        await WithNodeAsync(async (root, source, destination, profile, sourceInventory, token) =>
        {
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);
            _ = ServerNodeFormatUpgrade.Prepare(source, options);
            var stage = destination + ServerNodeUpgradeProtocol.StageSuffix;
            await CorruptReceiptAsync(Path.Combine(stage, ServerNodeUpgradeProtocol.OwnerReceipt), token);
            var stageInventory = await NodeEpochInventoryCapture.CaptureAsync(stage, token);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.VerifyPrepared(source, options));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(Directory.Exists(destination)).IsFalse();
            await NodeEpochInventoryCapture.AssertUnchangedAsync(stage, stageInventory, token);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch010RefusesAnAlreadyOwnedOriginalNodeLockWithoutMutation()
    {
        await WithNodeAsync(async (root, source, destination, profile, sourceInventory, token) =>
        {
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);
            var lockPath = Path.Combine(source, ServerNodeUpgradeProtocol.NodeOwner);
            using (var held = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            { _ = Assert.ThrowsExactly<IOException>(() => ServerNodeFormatUpgrade.Prepare(source, options)); }

            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);
            await Assert.That(Directory.Exists(destination)).IsFalse();
            await Assert.That(Directory.Exists(destination + ServerNodeUpgradeProtocol.StageSuffix)).IsFalse();
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch010RejectsUnrelatedDestinationAndPreservesBothInventories()
    {
        await WithNodeAsync(async (root, source, destination, profile, sourceInventory, token) =>
        {
            Directory.CreateDirectory(destination);
            var foreignPath = Path.Combine(destination, "operator-data.bin");
            byte[] foreignBytes = [0xA1, 0x13, 0x00, 0x55];
            await File.WriteAllBytesAsync(foreignPath, foreignBytes, token);
            var destinationInventory = await NodeEpochInventoryCapture.CaptureAsync(destination, token);
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.Prepare(source, options));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Conflict);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(destination, destinationInventory, token);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);
            var retainedBytes = await File.ReadAllBytesAsync(foreignPath, token);
            await Assert.That(retainedBytes.SequenceEqual(foreignBytes)).IsTrue();
        }, TestContext.Current!.Execution.CancellationToken);
    }

    private static async Task CorruptReceiptAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        bytes[^1] ^= 0x01;
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
    }

    internal static async Task WithNodeAsync(
        Func<string, string, string, EpochPriorNodeProfile, NodeEpochInventory, CancellationToken, Task> action,
        CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, SourceName);
        var destination = Path.Combine(root, TargetName);
        var profile = NodeEpochComponentProfile.Create();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            _ = await EpochPriorExecutableFixture.CreateNodeAsync(source, profile, timeout.Token);
            var sourceInventory = await NodeEpochInventoryCapture.CaptureAsync(source, timeout.Token);
            await action(root, source, destination, profile, sourceInventory, timeout.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds));
            await EpochUpgradeCleanup.SettleNodeAsync(null, root, source, activeFailure, cleanup.Token);
        }
    }
}
