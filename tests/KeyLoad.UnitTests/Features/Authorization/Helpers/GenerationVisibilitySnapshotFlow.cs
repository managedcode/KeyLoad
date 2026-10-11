using KeyLoad.Core.Features.Authorization;
using KeyLoad.Query;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class GenerationVisibilitySnapshotFlow
{
    private const long GenerationAdvance = 1;
    private const string SnapshotPrefix = "visibility-generation-";
    private const string SnapshotSuffix = ".snapshot";
    private const string SnapshotCollision = "The fixture snapshot path is already owned.";

    internal static async Task RunAsync(TestDatabase db, QueryEngine queries,
        RowVisibilityCache cache, CancellationToken token)
    {
        RowVisibilityLease? original = null;
        RowVisibilityLease? unexpected = null;
        RowVisibilityLease? current = null;
        var failures = new List<Exception>();
        var snapshotOwned = false;
        var path = Path.Combine(db.Directory, SnapshotPrefix + Guid.NewGuid().ToString("N") + SnapshotSuffix);
        try
        {
            token.ThrowIfCancellationRequested();
            original = GenerationVisibilityAssertions.Hold(db, cache);
            if (File.Exists(path))
            { throw new InvalidOperationException(SnapshotCollision); }
            snapshotOwned = true;
            await InstallAsync(db, path);
            unexpected = AcquireCurrentGeneration(db, cache);
            await Assert.That(unexpected).IsNull();
            await ContentsAsync(original);
            await GenerationVisibilityAssertions.HealthyAsync(db, db.Database, queries, db.Store,
                GenerationVisibilityFlow.InitialRevision, TeamRowPolicyFlow.OriginalJson, token);
            current = GenerationVisibilityAssertions.Hold(db, cache);
            await ContentsAsync(current);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        { failures.Add(error); }
        finally { Close(original, unexpected, current, snapshotOwned, path, failures); }
        RowVisibilityFailureLedger.Throw(failures);
    }

    private static async Task InstallAsync(TestDatabase db, string path)
    {
        var identity = db.Store.Identity;
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var cut = db.Store.Position;
        var applied = db.Database.LastApplied;
        var snapshot = db.Store.CreateSnapshot(path, applied);
        await Assert.That(snapshot.Position).IsEqualTo(cut);
        await Assert.That(snapshot.AppliedPosition).IsEqualTo(applied);
        var installed = db.Store.InstallSnapshot(path, snapshot.AppliedPosition);
        await Assert.That(installed.Position).IsEqualTo(cut);
        await Assert.That(installed.AppliedPosition).IsEqualTo(applied);
        await Assert.That(db.Store.Identity.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(db.Store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
        await Assert.That(db.Store.Identity.ReadGeneration).IsEqualTo(identity.ReadGeneration + GenerationAdvance);
        await TeamRowPolicyAssertions.UnchangedAsync(db.Store, image, cut);
    }

    private static RowVisibilityLease? AcquireCurrentGeneration(TestDatabase db, RowVisibilityCache cache)
        => db.Database.WithQueryView(TeamRowPolicyFlow.Reader, db.Partition, TeamRowPolicyFlow.Collection,
            (view, principal, resource) => cache.Acquire(db.Database.CaptureRowVisibility(principal, resource,
                db.Partition, resource.Name, db.Database.DocumentEpoch(view, db.Partition, resource.Name))));

    private static async Task ContentsAsync(RowVisibilityLease lease)
    {
        await Assert.That(lease.Contains(TeamRowPolicyFlow.Row)).IsTrue();
        await Assert.That(lease.Contains(TeamRowPolicyFlow.ForeignTeam)).IsFalse();
    }

    private static void Close(RowVisibilityLease? original, RowVisibilityLease? unexpected,
        RowVisibilityLease? current, bool snapshotOwned, string path, List<Exception> failures)
    {
        if (current is not null)
        { RowVisibilityFailureLedger.Capture(current.Dispose, failures); }
        if (unexpected is not null)
        { RowVisibilityFailureLedger.Capture(unexpected.Dispose, failures); }
        if (original is not null)
        { RowVisibilityFailureLedger.Capture(original.Dispose, failures); }
        if (snapshotOwned)
        { RowVisibilityFailureLedger.Capture(() => File.Delete(path), failures); }
    }
}
