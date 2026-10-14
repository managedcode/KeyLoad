using KeyLoad.Clustering;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25CorpusPublicationFlow(TestDatabase database)
{
    internal const string Subject = "bm25-current-subject";
    internal const string Collection = "orders";
    internal const string Field = "/text";
    internal const string Term = "needle";
    internal const string Root = "root";
    internal TestDatabase Database => database;
    internal PrincipalRecord Principal { get; private set; } = null!;
    internal static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    internal static async Task RunAsync(Func<Bm25CorpusPublicationFlow, Task> execute)
    {
        var failures = new List<Exception>();
        try
        {
            using var database = new TestDatabase(nativeReplicaAdmission: true);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var flow = new Bm25CorpusPublicationFlow(database);
                flow.Seed();
                await execute(flow);
            }, failures);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void Seed()
    {
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "first", "{\"text\":\"needle\"}"));
        Principal = SetPrincipal(new(Subject, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], []));
    }

    internal PrincipalRecord SetPrincipal(PrincipalRecord replacement)
        => database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(replacement)).Get<PrincipalRecord>();

    internal ReplicatedOperation Capture(Guid id, Guid? expected, string text = Term, string subject = Subject)
        => Bm25CorpusCapture.Create(database.Database, Root, subject,
            new(database.Partition, Collection, Field, text), id, expected,
            database.Database.EvaluationClock.GetUtcNow().Add(UnitRoutingOptions.Routing().Value.RequestLifetime),
            database.CreateReadWork(Token), UnitExecutionOptions.QueryExecution());

    internal static Bm25CorpusPublicationPhaseCommand Phase(ReplicatedOperation operation)
        => NativeCommandPayload.Read<Bm25CorpusPublicationPhaseCommand>(operation);

    internal CommitReceipt Publish(ReplicatedOperation operation) => database.SubmitIssued(operation).Get<CommitReceipt>();

    internal KeyLoad.Query.Features.QueryExecution.DistributedTextStatisticsV1? Current(string term = Term)
        => database.Database.WithQueryView(Subject, database.Partition, Collection,
            (view, principal, resource) => database.Database.ReadBm25CorpusCurrent(view, principal, resource,
                database.Partition, Collection, Field, TextProjectionProtocol.TokenizerVersion, [term], database.CreateReadWork(Token)));

    internal Task<RankedDocument[]> SearchAsync()
        => new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(Subject,
            new(database.Partition, Collection, Field, Term), Token);

    internal void RequireAdmission(Bm25CorpusCurrentV1 current, DatabaseLimits limits)
    {
        limits.Validate();
        database.Store.Read(view =>
        {
            Bm25CorpusFamilyAdmission.Require(view, Bm25CorpusKeys.Current(current), current, limits);
            return true;
        });
    }
}
