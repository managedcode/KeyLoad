using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class GenerationVisibilityPartialScanFlow
{
    internal const string Reader = "visibility-partial-scan-reader";
    private const int RefusedOperations = 2;
    private const string MissingCache = "The shared budget refused the bounded scan owner.";
    private const string MissingDraft = "The current authorized scope refused its genuine draft.";
    private const string CandidateBudget = "The query access path exceeds its candidate budget.";
    private const string Scan = "bounded-full-scan";
    private const string AbsentRow = "visibility-not-seeded";

    internal static async Task RunAsync(TestDatabase db, CacheMemoryBudget memory, CancellationToken token)
    {
        await GenerationVisibilityFlow.ConfigureAsync(db, new(Reader, GenerationVisibilityFlow.Tenant,
            [new(GenerationVisibilityFlow.DatabaseScope, TeamRowPolicyFlow.Collection,
                Capability.DocumentsRead | Capability.Query)], [])
        {
            PolicyEpoch = GenerationVisibilityFlow.OriginalEpoch
        });
        var before = memory.GetSnapshot();
        var limited = CreateOwner(db);
        RowVisibilityCache? cache = null;
        var failures = new List<Exception>();
        try
        {
            cache = limited.ConfigureRowVisibility(memory) ?? throw new InvalidOperationException(MissingCache);
            await RefusedAsync(db, limited, cache, memory, token);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        finally
        {
            if (cache is not null)
            {
                RowVisibilityFailureLedger.Capture(cache.Dispose, failures);
            }
        }
        RowVisibilityFailureLedger.Throw(failures);
        await Assert.That(memory.GetSnapshot()).IsEqualTo(before);
        await HealthyAsync(db.Database, db.Store, db.Partition, GenerationVisibilityFlow.InitialRevision,
            TeamRowPolicyFlow.OriginalJson, GenerationVisibilityFlow.InitialRevision, token);
    }

    private static DatabaseEngine CreateOwner(TestDatabase db)
        => new(db.Store, db.Database.Authorization,
            UnitExecutionOptions.DatabaseLimits(db.Database.Limits with
            {
                MaxScanRecords = 1
            }),
            UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(),
            UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(),
            UnitExecutionOptions.RuntimeClaimsExecution(),
            UnitExecutionOptions.TimeSeriesExecution(), db.Database.EvaluationClock);

    private static RowVisibilityScope Scope(DatabaseEngine database, PartitionRef partition)
        => database.WithQueryView(Reader, partition, TeamRowPolicyFlow.Collection,
            (view, principal, resource) => database.CaptureRowVisibility(principal, resource, partition,
                resource.Name, database.DocumentEpoch(view, partition, resource.Name)));

    private static async Task RefusedAsync(TestDatabase db, DatabaseEngine limited, RowVisibilityCache cache,
        CacheMemoryBudget memory, CancellationToken token)
    {
        var scope = Scope(limited, db.Partition);
        var admitted = memory.GetSnapshot();
        using (var draft = cache.Begin(scope) ?? throw new InvalidOperationException(MissingDraft))
        {
            draft.AddVisible(TeamRowPolicyFlow.ForeignTeam);
        }
        await Assert.That(memory.GetSnapshot()).IsEqualTo(admitted);
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var identity = db.Store.Identity;
        var cut = db.Store.Position;
        var queries = new QueryEngine(limited, UnitExecutionOptions.QueryExecution());
        for (var operation = 0; operation < RefusedOperations; operation++)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute(Reader,
                new(db.Partition, GenerationVisibilityAssertions.Sql, AllowFullScan: true), cancellationToken: token));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(failure.Message).IsEqualTo(CandidateBudget);
            await Assert.That(memory.GetSnapshot()).IsEqualTo(admitted);
            using var absent = cache.Acquire(scope);
            await Assert.That(absent).IsNull();
            await TeamRowPolicyAssertions.UnchangedAsync(db.Store, image, cut);
            await Assert.That(db.Store.Identity).IsEqualTo(identity);
        }
    }

    internal static async Task HealthyAsync(DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        long revision, string json, long foreignRevision, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        QueryRow[] expected = [new(TeamRowPolicyFlow.ForeignTeam, foreignRevision,
            TeamRowPolicyFlow.OriginalJson, false, []), new(TeamRowPolicyFlow.Row, revision, json, false, [])];
        var page = queries.Execute(Reader, new(partition, GenerationVisibilityAssertions.Sql, AllowFullScan: true),
            cancellationToken: token);
        await Assert.That(JsonDefaults.Serialize(page.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
        var cache = database.RowVisibility ?? throw new InvalidOperationException(MissingCache);
        using var complete = cache.Acquire(Scope(database, partition))
            ?? throw new InvalidOperationException(MissingDraft);
        await Assert.That(complete.Contains(TeamRowPolicyFlow.ForeignTeam)).IsTrue();
        await Assert.That(complete.Contains(TeamRowPolicyFlow.Row)).IsTrue();
        await Assert.That(complete.Contains(AbsentRow)).IsFalse();
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }
}
