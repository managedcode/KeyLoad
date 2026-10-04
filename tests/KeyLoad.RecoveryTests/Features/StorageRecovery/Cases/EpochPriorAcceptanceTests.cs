using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class EpochPriorAcceptanceTests
{
    private const string TrialPrefix = "keyload-epoch-prior-refusal-";
    private const string CleanupFailureKey = "KeyLoad.EpochPriorCleanupFailure";
    private const int TrialTimeoutSeconds = 45;
    private const int CleanupTimeoutSeconds = 30;

    [Test]
    public async Task AcEpoch004ActualEpoch5ExecutableRejectsCurrent6StoreWithoutMutation()
    {
        await RunTrialAsync(async (root, token) =>
        {
            var source = Path.Combine(root, "source");
            var target = Path.Combine(root, "target");
            var original = await EpochPriorExecutableFixture.CreateAsync(source, false, token);
            var sourceFiles = await EpochUpgradeFileInventory.CaptureAsync(source, token);
            _ = ZoneTreeFormatUpgrade.Upgrade(source, new(target));
            var targetFiles = await EpochUpgradeFileInventory.CaptureAsync(target, token);

            var rejected = await EpochPriorExecutableFixture.InspectAsync(target, token);
            await Assert.That(rejected.ErrorCode).IsEqualTo(ErrorCode.FormatUnsupported.ToString());
            await AssertInventoriesUnchangedAsync(source, sourceFiles, target, targetFiles, token);
            var stillReadable = await EpochPriorExecutableFixture.InspectAsync(source, token);
            await AssertSameOriginalIdentityAsync(stillReadable, original);
            await EpochUpgradeFileInventory.AssertAuthorityUnchangedAsync(source, sourceFiles, token);
            await EpochUpgradeFileInventory.AssertUnchangedAsync(target, targetFiles, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch004ActualEpoch5CheckpointReaderRejectsCheckpoint4WithoutMutation()
    {
        await RunTrialAsync(async (root, token) =>
        {
            var source = Path.Combine(root, "source");
            var target = Path.Combine(root, "target");
            var original = await EpochPriorExecutableFixture.CreateAsync(source, false, token);
            var initialInspection = await EpochPriorExecutableFixture.InspectAsync(source, token);
            await AssertSameOriginalIdentityAsync(initialInspection, original);

            _ = ZoneTreeFormatUpgrade.Upgrade(source, new(target));
            var checkpoint = Path.Combine(target, "current-checkpoint4.bin");
            using (var current = new ZoneTreeStore(new(target)))
            {
                _ = current.CreateSnapshot(checkpoint, original.AppliedPosition);
            }
            var sourceFiles = await EpochUpgradeFileInventory.CaptureAsync(source, token);
            var targetFiles = await EpochUpgradeFileInventory.CaptureAsync(target, token);

            var rejected = await EpochPriorExecutableFixture.VerifySnapshotAsync(source, checkpoint, token);
            await Assert.That(rejected.ErrorCode).IsEqualTo(ErrorCode.FormatUnsupported.ToString());
            await AssertInventoriesUnchangedAsync(source, sourceFiles, target, targetFiles, token);
            var stillReadable = await EpochPriorExecutableFixture.InspectAsync(source, token);
            await AssertSameOriginalIdentityAsync(stillReadable, original);
            await EpochUpgradeFileInventory.AssertAuthorityUnchangedAsync(source, sourceFiles, token);
            await EpochUpgradeFileInventory.AssertUnchangedAsync(target, targetFiles, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch001CurrentExecutableRejectsEpoch5BeforeMutation()
    {
        await RunTrialAsync(async (root, token) =>
        {
            var source = Path.Combine(root, "source");
            _ = await EpochPriorExecutableFixture.CreateAsync(source, false, token);
            var sourceFiles = await EpochUpgradeFileInventory.CaptureAsync(source, token);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
            {
                using var unexpected = new ZoneTreeStore(new(source));
            });
            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.FormatUnsupported);
            await EpochUpgradeFileInventory.AssertUnchangedAsync(source, sourceFiles, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    private static async Task RunTrialAsync(Func<string, CancellationToken, Task> trial,
        CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TrialTimeoutSeconds));
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
            await CleanupTrialAsync(root, activeFailure);
        }
    }

    private static async Task CleanupTrialAsync(string root, Exception? activeFailure)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
        try
        {
            await StoragePublicationRecoveryTests.DeleteTrialAsync(root, cleanup.Token);
        }
        catch (Exception cleanupFailure)
        {
            if (activeFailure is null)
            {
                throw;
            }
            activeFailure.Data[CleanupFailureKey] = cleanupFailure;
        }
    }

    private static async Task AssertInventoriesUnchangedAsync(string source,
        Dictionary<string, string> sourceFiles, string target, Dictionary<string, string> targetFiles,
        CancellationToken cancellationToken)
    {
        await EpochUpgradeFileInventory.AssertUnchangedAsync(source, sourceFiles, cancellationToken);
        await EpochUpgradeFileInventory.AssertUnchangedAsync(target, targetFiles, cancellationToken);
    }

    private static async Task AssertSameOriginalIdentityAsync(EpochPriorProbeReceipt actual,
        EpochPriorProbeReceipt expected)
    {
        await Assert.That(actual.ErrorCode).IsNull();
        await Assert.That(actual.SourceRevision).IsEqualTo(expected.SourceRevision);
        await Assert.That(actual.DataEpoch).IsEqualTo(expected.DataEpoch);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.AppliedPosition).IsEqualTo(expected.AppliedPosition);
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.ReadGeneration).IsEqualTo(expected.ReadGeneration);
        await Assert.That(actual.DispatchPaused).IsEqualTo(expected.DispatchPaused);
        await Assert.That(actual.SigningKeySha256).IsEqualTo(expected.SigningKeySha256);
        await Assert.That(actual.Durability).IsEqualTo(expected.Durability);
    }
}
