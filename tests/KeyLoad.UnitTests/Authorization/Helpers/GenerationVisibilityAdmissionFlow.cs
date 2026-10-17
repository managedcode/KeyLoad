using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class GenerationVisibilityAdmissionFlow
{
    private const int MetadataEntries = 0;
    private const long NoRetainedBytes = 0;
    private const string AdmissionRequired = "The genuine remaining shared admission reservation was refused.";

    internal static Task RunAsync(TestDatabase db, CancellationToken token)
        => GenerationVisibilityFlow.RunAsync(db, token, admissionControls: true);

    internal static async Task ControlsAsync(TestDatabase db, QueryEngine queries, RowVisibilityCache cache,
        CacheMemoryBudget memory, CancellationToken token)
    {
        var before = memory.GetSnapshot();
        if (!memory.TryReserve(before.MaxRetainedBytes - before.RetainedBytes, MetadataEntries, out var competing))
        {
            throw new InvalidOperationException(AdmissionRequired);
        }
        using (competing)
        {
            var occupied = memory.GetSnapshot();
            await GenerationVisibilityAssertions.HealthyAsync(db, db.Database, queries, db.Store,
                GenerationVisibilityFlow.InitialRevision, TeamRowPolicyFlow.OriginalJson, token);
            await Assert.That(memory.GetSnapshot()).IsEqualTo(occupied);
        }
        await Assert.That(memory.GetSnapshot()).IsEqualTo(before);
        await GenerationVisibilityAssertions.HealthyAsync(db, db.Database, queries, db.Store,
            GenerationVisibilityFlow.InitialRevision, TeamRowPolicyFlow.OriginalJson, token);
        using var held = GenerationVisibilityAssertions.Hold(db, cache);
        var retained = memory.GetSnapshot();
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var cut = db.Store.Position;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => queries.Execute(TeamRowPolicyFlow.Reader,
            new(db.Partition, GenerationVisibilityAssertions.Sql, AllowFullScan: true), cancellationToken: cancelled.Token));
        await TeamRowPolicyAssertions.UnchangedAsync(db.Store, image, cut);
        await Assert.That(memory.GetSnapshot()).IsEqualTo(retained);
        await Assert.That(held.Contains(TeamRowPolicyFlow.Row)).IsTrue();
        await GenerationVisibilityAssertions.HealthyAsync(db, db.Database, queries, db.Store,
            GenerationVisibilityFlow.InitialRevision, TeamRowPolicyFlow.OriginalJson, token);
        await Assert.That(memory.GetSnapshot().RetainedBytes).IsGreaterThan(NoRetainedBytes);
        await GenerationVisibilityPartialScanFlow.RunAsync(db, memory, token);
    }
}
