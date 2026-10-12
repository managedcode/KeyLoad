using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25ConfiguredQuotaFlow(TestDatabase database)
{
    internal const int HeadCount = 8;
    internal const string Collection = "orders";
    internal const string Text = "needle extra";
    internal TestDatabase Database => database;
    internal static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    internal static async Task RunAsync(int? capacity, Func<Bm25ConfiguredQuotaFlow, Task> body)
    {
        var failures = new List<Exception>();
        try
        {
            var limits = capacity.HasValue ? new DatabaseLimits { MaxBatchBytes = capacity.Value } : new DatabaseLimits();
            limits.Validate();
            using var database = new TestDatabase(limits, nativeReplicaAdmission: true);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var flow = new Bm25ConfiguredQuotaFlow(database);
                flow.Seed();
                await body(flow);
            }, failures);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void Seed()
    {
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "first", "{\"text\":\"needle extra\"}"));
        for (var index = 0; index < HeadCount; index++)
        {
            var principal = new PrincipalRecord(Subject(index), database.Partition.TenantId,
                [new(database.Partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], []);
            database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        }
    }

    internal ReplicatedOperation Capture(int index, Guid? expected = null, string text = Text)
        => Bm25CorpusCapture.Create(database.Database, "root", Subject(index),
            new(database.Partition, Collection, "/text", text), Guid.NewGuid(), expected,
            database.Database.EvaluationClock.GetUtcNow().Add(UnitRoutingOptions.Routing().Value.RequestLifetime),
            database.CreateReadWork(Token), UnitExecutionOptions.QueryExecution());

    internal static Bm25CorpusPublicationPhaseCommand Phase(ReplicatedOperation operation)
        => NativeCommandPayload.Read<Bm25CorpusPublicationPhaseCommand>(operation);

    internal OperationResult Publish(ReplicatedOperation operation) => database.SubmitIssued(operation);

    internal static string Subject(int index) => "bm25-quota-subject-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
