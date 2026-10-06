using KeyLoad.CrashHost;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NodeEpochPathValidationTests
{
    private const string TrialPrefix = "keyload-node-epoch-path-";
    private const string SourceName = "prior-node";
    private const string TargetName = "current-node";
    private const int CleanupSeconds = 30;

    [Test]
    public async Task AcEpoch011RejectsSameNestedAndCaseVariedSourceTargetsBeforeMutation()
    {
        await RunTrialAsync(async (root, source, profile, inventory, token) =>
        {
            var nested = Path.Combine(source, "nested-target");
            var caseVariedNested = source.ToUpperInvariant() + Path.DirectorySeparatorChar + "case-target";
            // Linux rejects the absent parent; case-insensitive systems must also reject its source alias.
            await AssertInvalidPathAsync(source, source, profile, inventory, token);
            await AssertInvalidPathAsync(source, nested, profile, inventory, token);
            await AssertInvalidPathAsync(source, caseVariedNested, profile, inventory, token);
            await Assert.That(Directory.Exists(nested)).IsFalse();
            await Assert.That(Directory.Exists(caseVariedNested)).IsFalse();
        }, cancellationToken: TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch011RejectsSourceNestedUnderCurrentStageBeforeMutation()
    {
        using var admission = await StorageTrialLease.AcquireAsync(TestContext.Current!.Execution.CancellationToken);
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(root, TargetName);
        var stage = destination + ServerNodeUpgradeProtocol.StageSuffix;
        var source = Path.Combine(stage, SourceName);
        var profile = NodeEpochComponentProfile.Create();
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(stage);
            _ = await EpochPriorExecutableFixture.CreateNodeAsync(source, profile,
                TestContext.Current!.Execution.CancellationToken);
            var sourceInventory = await NodeEpochInventoryCapture.CaptureAsync(source,
                TestContext.Current!.Execution.CancellationToken);
            var stageInventory = await NodeEpochInventoryCapture.CaptureAsync(stage,
                TestContext.Current!.Execution.CancellationToken);
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.Prepare(source, RecoveryServerRuntimeOptions.Runtime(options)));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(Directory.Exists(destination)).IsFalse();
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, sourceInventory,
                TestContext.Current!.Execution.CancellationToken);
            await NodeEpochInventoryCapture.AssertUnchangedAsync(stage, stageInventory,
                TestContext.Current!.Execution.CancellationToken);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds), TimeProvider.System);
            await EpochUpgradeCleanup.SettleNodeAsync(null, root, source, activeFailure, cleanup.Token);
        }
    }

    private static async Task AssertInvalidPathAsync(string source, string destination, EpochPriorNodeProfile profile,
        NodeEpochInventory inventory, CancellationToken cancellationToken)
    {
        var options = NodeEpochCrashSettings.CreateOptions(profile, destination);
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.Prepare(source, RecoveryServerRuntimeOptions.Runtime(options)));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Validation);
        await NodeEpochInventoryCapture.AssertUnchangedAsync(source, inventory, cancellationToken);
    }

    private static async Task RunTrialAsync(
        Func<string, string, EpochPriorNodeProfile, NodeEpochInventory, CancellationToken, Task> trial,
        CancellationToken cancellationToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, SourceName);
        var profile = NodeEpochComponentProfile.Create();
        using var timeoutTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTimeout.Token);
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            _ = await EpochPriorExecutableFixture.CreateNodeAsync(source, profile, timeout.Token);
            var inventory = await NodeEpochInventoryCapture.CaptureAsync(source, timeout.Token);
            await trial(root, source, profile, inventory, timeout.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds), TimeProvider.System);
            await EpochUpgradeCleanup.SettleNodeAsync(null, root, source, activeFailure, cleanup.Token);
        }
    }
}
