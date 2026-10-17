using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>AC-CQ-026: concurrent synchronous shutdown joins real native readers and permits reopen.</summary>
internal sealed class NativeTextProjectionDisposalTests
{
    private const string Collection = "native-text-concurrent-disposal";
    private const string Field = "/text";
    private const string Query = "needle";
    private const string Principal = "root";
    private const string DocumentId = "one";
    private const string Payload = "{\"text\":\"needle original\"}";
    private const int Callers = 8;
    private const int AdmissionPollMilliseconds = 1;
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    [Test]
    public async Task ConcurrentDisposeDrainsBorrowedNativeLeaseAndPreservesSearchAfterReopen()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, DocumentId, Payload));
        var root = Path.Combine(database.Directory, Collection);
        using var projection = new NativeTextProjection(root, UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());
        var request = new SearchRequest(database.Partition, Collection, Field, Query);
        var cancellation = TestContext.Current!.Execution.CancellationToken;
        var original = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync(Principal, request, cancellation);
        await Assert.That(original).HasSingleItem();
        await DrainConcurrentCallersAsync(database, projection, original[0], cancellation);
        projection.Dispose();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
            new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync(Principal, request, cancellation));
        using var reopened = new NativeTextProjection(root, UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());
        var recovered = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), reopened).SearchAsync(Principal, request, cancellation);
        await Assert.That(recovered).HasSingleItem();
        await Assert.That(recovered[0].Document.Reference).IsEqualTo(original[0].Document.Reference);
        await Assert.That(recovered[0].Document.Revision).IsEqualTo(original[0].Document.Revision);
        await Assert.That(recovered[0].Document.Json).IsEqualTo(Payload);
    }

    private static async Task DrainConcurrentCallersAsync(TestDatabase database, NativeTextProjection projection,
        RankedDocument document, CancellationToken cancellation)
    {
        using var deadlineTimeout = new CancellationTokenSource(WaitLimit, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadlineTimeout.Token);
        var scope = CaptureScope(database);
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        using var lease = projection.Acquire(scope, budget);
        lease.BeginRecord(document.Document.Reference, document.Document.Revision);
        lease.ObserveToken(Query);
        var disposals = Enumerable.Range(0, Callers).Select(_ => Task.Factory.StartNew(projection.Dispose,
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await WaitForClosedAdmissionAsync(projection, scope, database.Database.Limits, deadline.Token);
            await Assert.That(disposals.All(task => !task.IsCompleted)).IsTrue();
            lease.VerifyCandidates([Query], [document.Document.Reference], budget);
        }, failures);
        ServerFailureObserver.Observe(lease.Dispose, failures);
        await ServerFailureObserver.ObserveAsync(() => Task.WhenAll(disposals).WaitAsync(WaitLimit, TimeProvider.System), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaitForClosedAdmissionAsync(NativeTextProjection projection, TextProjectionScope scope,
        DatabaseLimits limits, CancellationToken cancellation)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                using var rejected = projection.Acquire(scope, new(UnitExecutionOptions.DatabaseLimits(limits)));
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.BudgetExceeded)
            {
                // The held native lease saturates this generation until shutdown fences admission.
            }
            await Task.Delay(TimeSpan.FromMilliseconds(AdmissionPollMilliseconds), TimeProvider.System, cancellation);
        }
    }

    private static TextProjectionScope CaptureScope(TestDatabase database)
    {
        var identity = database.Store.Identity;
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Principal)))!;
        var resource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            database.Partition.TenantId, database.Partition.DatabaseId, Collection)))!;
        return new(identity.NodeId, identity.Incarnation, identity.FormatVersion, identity.ReadGeneration,
            database.Store.Position, database.Partition, Collection, Field, principal.Id, principal.PolicyEpoch,
            resource.SchemaVersion);
    }
}
