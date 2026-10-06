using KeyLoad.CrashHost;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NodeEpochCoordinatorAcceptanceTests
{
    private const string TrialPrefix = "keyload-node-epoch-component-";
    private const string SourceName = "prior-node";
    private const string TargetName = "current-node";
    private const int TimeoutSeconds = 120;
    private const int CleanupSeconds = 30;
    [Test]
    [Arguments(5)]
    [Arguments(6)]
    public async Task AcEpoch7PreparesThenPublishesEveryHistoricalImageAndPreservesLaterWritesOnRetry(int dataEpoch)
    {
        await RunTrialAsync(async (root, token) =>
        {
            var source = Path.Combine(root, SourceName);
            var destination = Path.Combine(root, TargetName);
            var profile = NodeEpochComponentProfile.Create();
            var prior = await EpochPriorExecutableFixture.CreateNodeAsync(source, profile, token, dataEpoch);
            var sourceInventory = await NodeEpochInventoryCapture.CaptureAsync(source, token);
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);

            var prepared = ServerNodeFormatUpgrade.Prepare(source, RecoveryServerRuntimeOptions.Runtime(options));
            await AssertPreparedStateAsync(destination, prepared, prior);
            var verified = ServerNodeFormatUpgrade.VerifyPrepared(source, RecoveryServerRuntimeOptions.Runtime(options));
            await AssertReceiptAsync(verified, prior);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);

            var published = ServerNodeFormatUpgrade.Publish(source, RecoveryServerRuntimeOptions.Runtime(options));
            await AssertReceiptAsync(published, prior);
            await Assert.That(Directory.Exists(destination)).IsTrue();
            await Assert.That(Directory.Exists(destination + ServerNodeUpgradeProtocol.StageSuffix)).IsFalse();
            await NodeEpochCoordinatorAssertions.AssertHistoricalImagesAsync(source, destination, prior, token);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);

            await NodeEpochCoordinatorAssertions.ApplyLaterOperationAsync(profile, destination, token);
            var targetAfterWrite = await NodeEpochInventoryCapture.CaptureAsync(destination, token);
            var retry = ServerNodeFormatUpgrade.Prepare(source, RecoveryServerRuntimeOptions.Runtime(options));
            await AssertReceiptAsync(retry, prior);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(destination, targetAfterWrite, token);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);
            await NodeEpochCoordinatorAssertions.AssertLaterResourceAsync(profile, destination);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch008RejectsIncomingSnapshotEvidenceBeforeAnyTargetPublication()
    {
        await RunTrialAsync(async (root, token) =>
        {
            var source = Path.Combine(root, SourceName);
            var destination = Path.Combine(root, TargetName);
            var profile = NodeEpochComponentProfile.Create();
            _ = await EpochPriorExecutableFixture.CreateNodeAsync(source, profile, token);
            var incoming = Path.Combine(source, "snapshots", "incoming.snapshot.tmp");
            byte[] incomingBytes = [0x51, 0x00];
            await File.WriteAllBytesAsync(incoming, incomingBytes, token);
            var sourceInventory = await NodeEpochInventoryCapture.CaptureAsync(source, token);
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.Prepare(source, RecoveryServerRuntimeOptions.Runtime(options)));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.RecoveryRequired);
            await Assert.That(Directory.Exists(destination)).IsFalse();
            await Assert.That(Directory.Exists(destination + ServerNodeUpgradeProtocol.StageSuffix)).IsFalse();
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch008PreservesUnknownSourceArtifactsAndRefusesPublication()
    {
        await RunTrialAsync(async (root, token) =>
        {
            var source = Path.Combine(root, SourceName);
            var destination = Path.Combine(root, TargetName);
            var profile = NodeEpochComponentProfile.Create();
            _ = await EpochPriorExecutableFixture.CreateNodeAsync(source, profile, token);
            byte[] unexpectedBytes = [0x31, 0x72];
            await File.WriteAllBytesAsync(Path.Combine(source, "unrecognized.node-data"), unexpectedBytes, token);
            var sourceInventory = await NodeEpochInventoryCapture.CaptureAsync(source, token);
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.Prepare(source, RecoveryServerRuntimeOptions.Runtime(options)));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.FormatUnsupported);
            await Assert.That(Directory.Exists(destination)).IsFalse();
            await Assert.That(Directory.Exists(destination + ServerNodeUpgradeProtocol.StageSuffix)).IsFalse();
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch009CorruptPreparedReceiptIsPreservedAndCannotBePublished()
    {
        await RunTrialAsync(async (root, token) =>
        {
            var source = Path.Combine(root, SourceName);
            var destination = Path.Combine(root, TargetName);
            var stage = destination + ServerNodeUpgradeProtocol.StageSuffix;
            var profile = NodeEpochComponentProfile.Create();
            _ = await EpochPriorExecutableFixture.CreateNodeAsync(source, profile, token);
            var sourceInventory = await NodeEpochInventoryCapture.CaptureAsync(source, token);
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);
            _ = ServerNodeFormatUpgrade.Prepare(source, RecoveryServerRuntimeOptions.Runtime(options));
            var preparedReceipt = Path.Combine(stage, ServerNodeUpgradeProtocol.PreparedReceipt);
            var bytes = await File.ReadAllBytesAsync(preparedReceipt, token);
            bytes[^1] ^= 0x01;
            await File.WriteAllBytesAsync(preparedReceipt, bytes, token);
            var stageInventory = await NodeEpochInventoryCapture.CaptureAsync(stage, token);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.VerifyPrepared(source, RecoveryServerRuntimeOptions.Runtime(options)));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(Directory.Exists(destination)).IsFalse();
            await NodeEpochInventoryCapture.AssertUnchangedAsync(stage, stageInventory, token);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    private static async Task AssertPreparedStateAsync(string destination, ServerNodeUpgradeReceipt prepared,
        EpochPriorProbeReceipt prior)
    {
        await AssertReceiptAsync(prepared, prior);
        await Assert.That(Directory.Exists(destination)).IsFalse();
        await Assert.That(Directory.Exists(destination + ServerNodeUpgradeProtocol.StageSuffix)).IsTrue();
    }

    private static async Task AssertReceiptAsync(ServerNodeUpgradeReceipt actual, EpochPriorProbeReceipt prior)
    {
        await Assert.That(actual.SourceEpoch).IsEqualTo(prior.DataEpoch);
        await Assert.That(actual.TargetEpoch).IsEqualTo(7);
        await Assert.That(actual.CanonicalNodeId).IsEqualTo(prior.NodeId);
        await Assert.That(actual.ReplicaNodeId).IsNotEqualTo(actual.CanonicalNodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(prior.Incarnation);
        await Assert.That(actual.CanonicalPosition).IsEqualTo(prior.Position);
        await Assert.That(actual.CanonicalAppliedPosition).IsEqualTo(3L);
        await Assert.That(actual.OriginalReplicaHardState.Term).IsEqualTo(1L);
        await Assert.That(actual.OriginalReplicaHardState.LastIndex).IsEqualTo(3L);
        await Assert.That(actual.OriginalReplicaHardState.CommittedIndex).IsEqualTo(3L);
        await Assert.That(actual.OriginalReplicaHardState.Snapshot?.Index).IsEqualTo(3L);
    }

    private static async Task RunTrialAsync(Func<string, CancellationToken, Task> trial,
        CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        using var timeoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutTimeout.Token);
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            await trial(root, timeout.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds), TimeProvider.System);
            await EpochUpgradeCleanup.SettleNodeAsync(null, root, Path.Combine(root, SourceName), activeFailure,
                cleanup.Token);
        }
    }
}
