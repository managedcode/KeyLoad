using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DatabaseComposition;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.DocumentStorage;

internal static class DocumentPathDiagnosticScenario
{
    private const string PointPath = "point";
    private const string EntityPath = "/@id";
    private const string NumberPath = "/number";
    private const int QueryOperations = 10_000;

    internal static async Task RunAsync(int records, int nodes, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = await FunctionalGateFixture.StartAsync(nodes, nodes, deadline.Token);
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(fixture, records, nodes, deadline.Token), failures);
            fixture.PreserveEvidence = failures.Count != 0;
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(FunctionalGateFixture fixture, int records, int nodes, CancellationToken token)
    {
        var output = Path.Combine(AppContext.BaseDirectory, "performance-investigation",
            "documents-" + records + "-n" + nodes + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        await Console.Out.WriteLineAsync("Local SDK diagnostic evidence: " + output);
        using var http = McpCallerHttp.Create(fixture.App, fixture.NodeNames[0]);
        http.DefaultRequestVersion = System.Net.HttpVersion.Version20;
        http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var partition = DocumentPathDiagnosticCorpus.Partition();
        _ = ClusterReplicationTestSupport.Success(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId,
                new(DocumentPathDiagnosticCorpus.Collection, ResourceKind.Collection, partition.TransactionDomainId)), token));
        var phases = new List<DocumentPathDiagnosticPhase>();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await MeasureAsync(output, phases, "bulk-create", records, () => LoadAsync(sdk, partition, records, nodes, token));
            await MeasureAsync(output, phases, "random-point-read", records, () => ReadAsync(sdk, partition, records, token));
            await MeasureAsync(output, phases, "point-predicate-query", QueryOperations,
                () => QueryAsync(sdk, partition, records, token));
            await MeasureAsync(output, phases, "mixed-point-read-update", records,
                () => DocumentPathDiagnosticMixed.ExecuteAsync(sdk, partition, records, nodes, token));
            await DocumentPathDiagnosticMixed.VerifyAsync(sdk, partition, records, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(Path.Combine(output, "phases.json"), JsonDefaults.Serialize(phases), CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(output, "active-phase.txt"), "settled", CancellationToken.None);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task LoadAsync(KeyLoadClient sdk, PartitionRef partition, int records, int nodes, CancellationToken token)
    {
        var batch = DocumentPathDiagnosticCorpus.BatchRecords;
        var batches = (records + batch - 1) / batch;
        await Parallel.ForEachAsync(Enumerable.Range(0, batches), Parallelism(token), async (index, cancellation) =>
        {
            var first = index * batch;
            var count = Math.Min(batch, records - first);
            var mutations = Enumerable.Range(first, count).Select(ordinal => (Mutation)new PutDocument(
                DocumentPathDiagnosticCorpus.Collection, DocumentPathDiagnosticCorpus.Id(ordinal), DocumentPathDiagnosticCorpus.Json(ordinal), 0))
                .ToImmutableArray();
            var receipt = ClusterReplicationTestSupport.Success(await sdk.CommitAsync(new(Guid.NewGuid(), partition, mutations), cancellation));
            if (receipt.PersistedCopies != nodes || receipt.Durability != DurabilityProfile.ProcessDurable || receipt.Mutations.Length != count)
            {
                throw new InvalidOperationException("The diagnostic bulk acknowledgement did not retain the required durable copies and mutation count.");
            }
        });
    }

    private static async Task ReadAsync(KeyLoadClient sdk, PartitionRef partition, int records, CancellationToken token)
    {
        await Parallel.ForEachAsync(Enumerable.Range(0, records), Parallelism(token), async (operation, cancellation) =>
        {
            var ordinal = (int)((long)operation * DocumentPathDiagnosticCorpus.ReadStride % records);
            var reference = new EntityRef(partition, DocumentPathDiagnosticCorpus.Collection, DocumentPathDiagnosticCorpus.Id(ordinal));
            var result = ClusterReplicationTestSupport.Success(await sdk.GetAsync(reference, cancellation));
            var document = result.Document ?? throw new InvalidOperationException("An acknowledged diagnostic document is missing.");
            DocumentPathDiagnosticCorpus.Require(document.Reference.Id, document.Json, document.Revision, ordinal, updated: false);
        });
    }

    private static async Task QueryAsync(KeyLoadClient sdk, PartitionRef partition, int records, CancellationToken token)
    {
        await Parallel.ForEachAsync(Enumerable.Range(0, QueryOperations), Parallelism(token), async (operation, cancellation) =>
        {
            var ordinal = (int)((long)operation * DocumentPathDiagnosticCorpus.ReadStride % records);
            var filter = new Logical(new Comparison(new FieldOperand(EntityPath), "=", ValueOperand.Create(DocumentPathDiagnosticCorpus.Id(ordinal))), "AND",
                new Comparison(new FieldOperand(NumberPath), "=", ValueOperand.Create(ordinal)));
            var page = ClusterReplicationTestSupport.Success(await sdk.QueryAstAsync(new(partition,
                new(DocumentPathDiagnosticCorpus.Collection, null, [new("*", "*")], filter, [new(EntityPath, false)], 1)), cancellation));
            if (page.AccessPath != PointPath || page.Rows.Length != 1 || page.Cursor is not null || page.Rows[0].Redacted)
            {
                throw new InvalidOperationException("The diagnostic point-predicate query has an incorrect access path or result cardinality.");
            }
            DocumentPathDiagnosticCorpus.Require(page.Rows[0].EntityId, page.Rows[0].Json, page.Rows[0].Revision, ordinal, updated: false);
        });
    }

    private static ParallelOptions Parallelism(CancellationToken token)
        => new() { MaxDegreeOfParallelism = DocumentPathDiagnosticCorpus.Clients, CancellationToken = token };

    private static async Task MeasureAsync(string output, List<DocumentPathDiagnosticPhase> phases, string name, int operations, Func<Task> run)
    {
        await File.WriteAllTextAsync(Path.Combine(output, "active-phase.txt"), name, CancellationToken.None);
        await Console.Out.WriteLineAsync($"Local SDK diagnostic phase={name} operations={operations}");
        var started = TimeProvider.System.GetTimestamp();
        await run();
        var elapsed = TimeProvider.System.GetElapsedTime(started).TotalSeconds;
        phases.Add(new(name, operations, elapsed, operations / elapsed));
        await Console.Out.WriteLineAsync($"Local SDK diagnostic phase={name} seconds={elapsed:F3} ops/s={operations / elapsed:F0}");
    }
}
