using KeyLoad.Clustering;
using KeyLoad.Core.Search;
using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Server;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Search;

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
            using var database = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var flow = new Bm25CorpusPublicationFlow(database);
                flow.Seed();
                await execute(flow);
            }, failures);
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
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

    internal Command<DatabaseCommand> Capture(Guid id, Guid? expected, string text = Term, string subject = Subject)
        => Bm25CorpusCapture.Create(database.Database, Root, subject,
            new(database.Partition, Collection, Field, text), id, expected,
            database.CreateReadWork(Token), UnitExecutionOptions.QueryExecution());

    internal static Bm25CorpusPublicationPhaseCommand Phase(Command<DatabaseCommand> operation)
        => ((Bm25CorpusPublicationPhaseCommand)(operation).Value!.Input);

    internal CommitReceipt Publish(Command<DatabaseCommand> operation) => database.SubmitIssued(operation).Get<CommitReceipt>();

    internal KeyLoad.Query.QueryExecution.DistributedTextStatistics? Current(string term = Term)
        => database.Database.WithQueryView(Subject, database.Partition, Collection,
            (view, principal, resource) => (database.Database.ReadBm25CorpusCurrent(view, principal, resource,
                database.Partition, Collection, Field, TextProjectionProtocol.TokenizerVersion, [term], database.CreateReadWork(Token))).Value);

    internal Task<RankedDocument[]> SearchAsync()
        => new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(Subject,
            new(database.Partition, Collection, Field, Term), Token);
}
