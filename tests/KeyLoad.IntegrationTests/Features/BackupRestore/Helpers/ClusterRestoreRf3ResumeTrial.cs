using System.Collections.Immutable;
using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Actual interrupted offline operation resumes before six-node admission, then all native callers continue.</summary>
internal static class ClusterRestoreRf3ResumeTrial
{
    private const string DurationLabel = "Actual original CLI process cut/refusals/resume/six-RF3/cold whole-flow duration: ";
    private const string DurationFormat = "c";
    private const string OffNodeDirectory = "off-node-resume";
    private const string UnavailableCredential = "unavailable-restore-operator";

    internal static async Task RunAsync(TwoRf3MembershipWave source, PartitionMovementPublicParentRf3Seed seed,
        string ownedRoot, NativeClusterRestoreStage stage, CancellationToken cancellationToken)
    {
        var secondary = await ClusterRestoreRf3SecondaryPartition.SeedAsync(source, seed, cancellationToken).ConfigureAwait(false);
        var eventing = await ClusterRestoreRf3EventingSeed.CreateAsync(source, seed, cancellationToken).ConfigureAwait(false);
        var originals = await ClusterRestoreRf3Capture.CaptureAsync(source, seed.Partition, Guid.NewGuid(),
            cancellationToken).ConfigureAwait(false);
        var archives = await ClusterRestoreRf3NativeArchive.CopyAsync(source, originals,
            Path.Combine(ownedRoot, OffNodeDirectory), cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3ColdCapture.RequireAsync(source, seed, originals, archives,
            cancellationToken).ConfigureAwait(false);
        var started = TimeProvider.System.GetTimestamp();
        var target = new ClusterRestoreRf3Fixture(ownedRoot, source.Profile.AdminKey, originals, archives);
        Exception? initiatingFailure = null;
        var cleanupCompleted = false;
        try
        {
            try
            {
                await RequireOperationAsync(target, seed, originals, archives, eventing, secondary,
                    stage, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception original)
            {
                // Retain the actual operation failure if the owning native shutdown also fails.
                initiatingFailure = original;
                throw;
            }
            finally { await target.DisposeAsync().ConfigureAwait(false); cleanupCompleted = true; }
        }
        catch (Exception terminal)
        {
            if (initiatingFailure is not null && !cleanupCompleted)
            { throw new AggregateException(initiatingFailure, terminal); }
            throw;
        }
        var elapsed = TimeProvider.System.GetElapsedTime(started);
        await Assert.That(elapsed).IsLessThan(ClusterRestoreRf3Protocol.ParentDeadline);
        await TestContext.Current!.OutputWriter.WriteLineAsync(DurationLabel + elapsed.ToString(DurationFormat, CultureInfo.InvariantCulture));
    }

    private static async Task RequireOperationAsync(ClusterRestoreRf3Fixture target,
        PartitionMovementPublicParentRf3Seed seed, ImmutableArray<ClusterBackupOwnerReceipt> originals,
        ImmutableArray<string> archives, ClusterRestoreRf3EventingState eventing, ClusterRestoreRf3SecondaryState secondary,
        NativeClusterRestoreStage stage, CancellationToken cancellationToken)
    {
        await target.StartHeldOperatorAsync(stage, cancellationToken).ConfigureAwait(false);
        await target.KillHeldAndJoinAsync(stage, cancellationToken).ConfigureAwait(false);
        if (stage != NativeClusterRestoreStage.PlanPublished)
        {
            await ClusterRestoreRf3MissingProgress.RequireAsync(target, Directory.Exists(target.DataRoot),
                cancellationToken).ConfigureAwait(false);
        }
        var originalPlan = ClusterRestoreRf3RetainedCut.Plan(target);
        var originalOperation = ClusterRestoreRf3RetainedCut.Operation(target);
        var published = Directory.Exists(target.DataRoot);
        await target.StartRejectedResumedCredentialAsync(UnavailableCredential, published, cancellationToken).ConfigureAwait(false);
        await Assert.That(ClusterRestoreRf3RetainedCut.Operation(target).SequenceEqual(originalOperation)).IsTrue();
        await ClusterRestoreRf3NativeArchive.RequireOriginalAsync(originals, archives, cancellationToken).ConfigureAwait(false);
        await target.StartRejectedPlanAsync(signer: true, published, cancellationToken).ConfigureAwait(false);
        await Assert.That(ClusterRestoreRf3RetainedCut.Operation(target).SequenceEqual(originalOperation)).IsTrue();
        await target.StartRejectedPlanAsync(signer: false, published, cancellationToken).ConfigureAwait(false);
        await Assert.That(ClusterRestoreRf3RetainedCut.Operation(target).SequenceEqual(originalOperation)).IsTrue();
        await ClusterRestoreRf3PlanCorruption.RequireAsync(target, published, cancellationToken).ConfigureAwait(false);
        await Assert.That(ClusterRestoreRf3RetainedCut.Operation(target).SequenceEqual(originalOperation)).IsTrue();
        await target.StartOperatorOnlyAsync(cancellationToken).ConfigureAwait(false);
        var nodes = await ClusterRestoreRf3TargetRead.RequireAsync(target, originals, dispatchPaused: true).ConfigureAwait(false);
        await target.RequireOperatorReceiptAsync(nodes).ConfigureAwait(false);
        var originalReceipt = await target.ReadOriginalReceiptAsync().ConfigureAwait(false);
        await Assert.That(ClusterRestoreRf3RetainedCut.Plan(target).SequenceEqual(originalPlan)).IsTrue();
        await ClusterRestoreRf3PlanCorruption.RequireAsync(target, published: true,
            ClusterRestoreRf3ResumeProtocol.ProgressFile, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3MissingProgress.RequireAsync(target, published: true, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3MissingSlot.RequireAsync(target, cancellationToken).ConfigureAwait(false);
        await target.StartOperatorOnlyAsync(cancellationToken).ConfigureAwait(false);
        var replayNodes = await ClusterRestoreRf3TargetRead.RequireAsync(target, originals, dispatchPaused: true).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(nodes, replayNodes);
        await target.RequireReceiptReplayAsync(originalReceipt).ConfigureAwait(false);
        await Assert.That(ClusterRestoreRf3RetainedCut.Plan(target).SequenceEqual(originalPlan)).IsTrue();
        await RequireWholeCallersAsync(target, seed, originals, eventing, secondary, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3NativeArchive.RequireOriginalAsync(originals, archives, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RequireWholeCallersAsync(ClusterRestoreRf3Fixture target,
        PartitionMovementPublicParentRf3Seed seed, ImmutableArray<ClusterBackupOwnerReceipt> originals,
        ClusterRestoreRf3EventingState eventing, ClusterRestoreRf3SecondaryState secondary, CancellationToken cancellationToken)
    {
        await target.StartAsync(restore: true, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3SecondaryPartition.RequireTargetAsync(target, seed.Credential, secondary, originals, cancellationToken).ConfigureAwait(false);
        var fresh = await ClusterRestoreRf3Scenario.CallersAsync(target, seed, originals, eventing, null, cancellationToken).ConfigureAwait(false);
        _ = await ClusterRestoreRf3TargetRead.RequireAsync(target, originals, dispatchPaused: false).ConfigureAwait(false);
        await target.StartAsync(restore: false, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3SecondaryPartition.RequireTargetAsync(target, seed.Credential, secondary, originals, cancellationToken).ConfigureAwait(false);
        _ = await ClusterRestoreRf3Scenario.CallersAsync(target, seed, originals, eventing, fresh, cancellationToken).ConfigureAwait(false);
        await target.StopAsync().ConfigureAwait(false);
        var before = ClusterRestoreRf3RetainedCut.Target(target);
        await target.StartRejectedTerminalAsync(cancellationToken).ConfigureAwait(false);
        await Assert.That(ClusterRestoreRf3RetainedCut.Target(target).SequenceEqual(before)).IsTrue();
        // The strict offline refusal leaves the genuine running product lineage available.
        await target.StartAsync(restore: false, cancellationToken).ConfigureAwait(false);
        _ = await ClusterRestoreRf3Scenario.CallersAsync(target, seed, originals, eventing, fresh, cancellationToken).ConfigureAwait(false);
    }
}
