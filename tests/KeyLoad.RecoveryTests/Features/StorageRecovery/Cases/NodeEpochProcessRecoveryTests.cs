using System.Diagnostics;
using System.Text.Json;
using KeyLoad.CrashHost;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NodeEpochProcessRecoveryTests
{
    private const string TrialPrefix = "keyload-node-epoch-crash-";
    private const string SourceName = "prior-node";
    private const string TargetName = "current-node";
    private const int TrialTimeoutSeconds = 180;
    private const int CleanupTimeoutSeconds = 30;

    [Test]
    [Arguments(NodeFormatUpgradeStage.SourceVerified, 5)]
    [Arguments(NodeFormatUpgradeStage.StoresConverted, 5)]
    [Arguments(NodeFormatUpgradeStage.ImagesConverted, 5)]
    [Arguments(NodeFormatUpgradeStage.DescriptorFlushed, 5)]
    [Arguments(NodeFormatUpgradeStage.TargetVerified, 5)]
    [Arguments(NodeFormatUpgradeStage.Published, 5)]
    [Arguments(NodeFormatUpgradeStage.SourceVerified, 6)]
    [Arguments(NodeFormatUpgradeStage.StoresConverted, 6)]
    [Arguments(NodeFormatUpgradeStage.ImagesConverted, 6)]
    [Arguments(NodeFormatUpgradeStage.DescriptorFlushed, 6)]
    [Arguments(NodeFormatUpgradeStage.TargetVerified, 6)]
    [Arguments(NodeFormatUpgradeStage.Published, 6)]
    public async Task AcEpoch009ActualNodeStageKillPreservesOriginalAndRetryPublishesCompleteNode(
        NodeFormatUpgradeStage stage, int dataEpoch)
    {
        await RunTrialAsync(stage, dataEpoch, TestContext.Current!.Execution.CancellationToken);
    }

    private static async Task RunTrialAsync(NodeFormatUpgradeStage stage, int dataEpoch, CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, SourceName);
        var destination = Path.Combine(root, TargetName);
        var profile = NodeEpochComponentProfile.Create();
        var options = NodeEpochCrashSettings.CreateOptions(profile, destination);
        Process? process = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TrialTimeoutSeconds));
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            var prior = await EpochPriorExecutableFixture.CreateNodeAsync(source, profile, timeout.Token, dataEpoch);
            var original = await NodeEpochInventoryCapture.CaptureAsync(source, timeout.Token);
            process = StartCrashProcess(source, destination, stage, profile);
            await AwaitBoundaryAsync(process, timeout.Token);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
            await NodeEpochFileReadiness.WaitAsync(source, timeout.Token);
            await AssertAfterKillAsync(source, destination, stage, original, timeout.Token);
            await RetryAndVerifyAsync(source, destination, options, profile, prior, original, timeout.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
            await EpochUpgradeCleanup.SettleNodeAsync(process, root, source, activeFailure, cleanup.Token);
        }
    }

    private static async Task AssertAfterKillAsync(string source, string destination, NodeFormatUpgradeStage stage,
        NodeEpochInventory original, CancellationToken cancellationToken)
    {
        await NodeEpochInventoryCapture.AssertUnchangedAsync(source, original, cancellationToken);
        await Assert.That(Directory.Exists(destination)).IsEqualTo(stage == NodeFormatUpgradeStage.Published);
        if (stage != NodeFormatUpgradeStage.Published)
        { await Assert.That(Directory.Exists(destination + ServerNodeUpgradeProtocol.StageSuffix)).IsTrue(); }
        else
        { await Assert.That(Directory.Exists(destination + ServerNodeUpgradeProtocol.StageSuffix)).IsFalse(); }
    }

    private static async Task RetryAndVerifyAsync(string source, string destination, NodeOptions options,
        EpochPriorNodeProfile profile, EpochPriorProbeReceipt prior, NodeEpochInventory original,
        CancellationToken cancellationToken)
    {
        var prepared = ServerNodeFormatUpgrade.Prepare(source, options);
        await Assert.That(prepared.TargetEpoch).IsEqualTo(7);
        await Assert.That(prepared.CanonicalNodeId).IsEqualTo(prior.NodeId);
        await Assert.That(prepared.Incarnation).IsEqualTo(prior.Incarnation);
        await Assert.That(prepared.CanonicalPosition).IsEqualTo(prior.Position);
        await Assert.That(prepared.CanonicalAppliedPosition).IsEqualTo(3L);
        var oldSource = await NodeEpochPriorInspection.InspectCopyAsync(source, original, cancellationToken);
        await Assert.That(oldSource.ErrorCode).IsNull();
        await Assert.That(oldSource.SourceRevision).IsEqualTo(prior.SourceRevision);
        await Assert.That(oldSource.DataEpoch).IsEqualTo(prior.DataEpoch);
        await Assert.That(oldSource.NodeId).IsEqualTo(prior.NodeId);
        await Assert.That(oldSource.AppliedPosition).IsEqualTo(prior.AppliedPosition);
        _ = ServerNodeFormatUpgrade.VerifyPrepared(source, options);
        _ = ServerNodeFormatUpgrade.Publish(source, options);
        await Assert.That(Directory.Exists(destination)).IsTrue();
        await Assert.That(Directory.Exists(destination + ServerNodeUpgradeProtocol.StageSuffix)).IsFalse();
        await NodeEpochInventoryCapture.AssertUnchangedAsync(source, original, cancellationToken);
        await NodeEpochCoordinatorAssertions.AssertHistoricalImagesAsync(source, destination, prior, cancellationToken);
        await NodeEpochCoordinatorAssertions.ApplyLaterOperationAsync(profile, destination,
            cancellationToken);
        var targetAfterWrite = await NodeEpochInventoryCapture.CaptureAsync(destination, cancellationToken);
        var retry = ServerNodeFormatUpgrade.Prepare(source, options);
        await Assert.That(retry.TargetEpoch).IsEqualTo(7);
        await NodeEpochInventoryCapture.AssertUnchangedAsync(destination, targetAfterWrite, cancellationToken);
        await NodeEpochInventoryCapture.AssertUnchangedAsync(source, original, cancellationToken);
        await NodeEpochCoordinatorAssertions.AssertLaterResourceAsync(profile, destination);
    }

    private static Process StartCrashProcess(string source, string destination, NodeFormatUpgradeStage stage,
        EpochPriorNodeProfile profile)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
        {
            typeof(CrashHostMarker).Assembly.Location,
            source,
            destination,
            stage.ToString(),
            NodeEpochCrashScenario.Mode
        })
        { start.ArgumentList.Add(argument); }
        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start node-epoch CrashHost.");
        process.StandardInput.WriteLine(JsonSerializer.Serialize(profile, EpochPriorSourceProbe.JsonOptions));
        process.StandardInput.Flush();
        process.StandardInput.Close();
        return process;
    }

    private static async Task AwaitBoundaryAsync(Process process, CancellationToken cancellationToken)
    {
        var marker = await process.StandardOutput.ReadLineAsync(cancellationToken);
        if (!string.Equals(marker, CrashFixtureValues.CrashMarker, StringComparison.Ordinal))
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException("The node-upgrade process missed its boundary: " + error);
        }
    }
}
