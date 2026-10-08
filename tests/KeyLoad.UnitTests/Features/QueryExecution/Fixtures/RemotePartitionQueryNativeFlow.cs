using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Executes the original bounded native leaf/merge over two independently owned real ZoneTree stores.</summary>
internal sealed class RemotePartitionQueryNativeFlow(RemoteDocumentNativeFixture fixture, TimeProvider? sourceClock = null, TimeProvider? destinationClock = null)
{
    private const string SourcePartitionKey = "source";
    private const string TitlePath = "/title";
    private const string TitleAlias = "title";
    private const string AuthorityChangedDetail = "The remote partition query authority changed.";
    internal const string SourceJson = "{\"title\":\"source\",\"secret\":\"source-private-canary\"}";
    internal const string SourceProjected = "{\"title\":\"source\"}";
    internal const long SourceEpoch = 3;
    internal const long DestinationEpoch = 2;
    internal const int RowLimit = 2;
    internal static PartitionRef Local => RemoteDocumentNativeFixture.Partition with { PartitionKey = SourcePartitionKey };
    private readonly List<(PartitionQueryLeafPlanV1 Plan, PartitionQuerySourceFence Fence,
        ReadExecutionBudgetReadGrant Grant)> fences = [];
    private readonly QueryEngine source = new(fixture.Source.Database, UnitExecutionOptions.QueryExecution());
    private readonly QueryEngine destination = new(fixture.Destination.Database, UnitExecutionOptions.QueryExecution());

    internal void Seed()
    {
        fixture.Seed();
        var permissions = new PrincipalRecord(RemoteDocumentNativeFixture.Reader, RemoteDocumentNativeFixture.Tenant,
            [new(Local.DatabaseId, RemoteDocumentNativeFixture.Collection, Capability.Query | Capability.DocumentsRead)], []);
        Configure(fixture.Source, permissions with { PolicyEpoch = DestinationEpoch });
        Configure(fixture.Source, permissions with { PolicyEpoch = SourceEpoch });
        var id = Guid.NewGuid();
        _ = fixture.Source.Database.Apply(RemoteDocumentNativeFixture.Operation(fixture.Source, OperationKind.Batch,
            id, new CommandRequest(id, Local, [new PutDocument(RemoteDocumentNativeFixture.Collection,
                RemoteDocumentNativeFixture.DocumentId, SourceJson)]))).Get<CommitReceipt>();
    }

    internal void GrantDestination()
        => Configure(fixture.Destination, new(RemoteDocumentNativeFixture.Reader, RemoteDocumentNativeFixture.Tenant,
            [new(Local.DatabaseId, RemoteDocumentNativeFixture.Collection, Capability.Query | Capability.DocumentsRead)], [])
        { PolicyEpoch = DestinationEpoch });

    internal void SetSourceGrant(long epoch, bool granted)
        => Configure(fixture.Source, new(RemoteDocumentNativeFixture.Reader, RemoteDocumentNativeFixture.Tenant,
            granted ? [new(Local.DatabaseId, RemoteDocumentNativeFixture.Collection,
                Capability.Query | Capability.DocumentsRead)] : [], [])
        { PolicyEpoch = epoch });

    internal void ChangeSourceResourcePolicy(long expectedVersion, long replacementVersion, string classification)
    {
        var definition = new ResourceDefinition(RemoteDocumentNativeFixture.Collection, ResourceKind.Collection,
            RemoteDocumentNativeFixture.Partition.TransactionDomainId)
        { SchemaVersion = replacementVersion, FieldPolicies = [new(RemoteDocumentNativeFixture.Secret, classification)] };
        var request = new ConfigureResourceRequest(RemoteDocumentNativeFixture.Tenant,
            Local.DatabaseId, definition)
        { ExpectedSchemaVersion = expectedVersion };
        _ = fixture.Source.Database.Apply(RemoteDocumentNativeFixture.Operation(fixture.Source,
            OperationKind.ConfigureResource, Guid.NewGuid(), request)).Get<ResourceDefinition>();
    }

    private static void Configure(PhysicalShardCatalogFixture owner, PrincipalRecord principal)
        => _ = owner.Database.Apply(RemoteDocumentNativeFixture.Operation(owner, OperationKind.ConfigurePrincipal,
            Guid.NewGuid(), new ConfigurePrincipalRequest(principal))).Get<PrincipalRecord>();

    internal Task<PartitionQueryPageV1> ExecuteAsync(CancellationToken token,
        Func<Task>? afterFirstActualLeaf = null, Func<int, Task>? afterActualLeaf = null)
    {
        fences.Clear();
        var completed = 0;
        var request = new PartitionQueryRequestV1(RemoteDocumentNativeFixture.Version,
            [Local, RemoteDocumentNativeFixture.Partition], new(RemoteDocumentNativeFixture.Collection, null,
                [new(TitlePath, TitleAlias)], null, [new(TitlePath, false)], RowLimit), null, true,
            RemoteDocumentNativeFixture.Version);
        return PartitionQueryRemoteExecution.ExecuteAsync(source, request,
            ReadAsync, RevalidateAsync, fixture.Source.Database.EvaluationClock, token);

        PartitionQueryPreparedLeaf ReadAsync(PartitionQueryLeafPlanV1 plan,
            ReadExecutionBudgetReadGrant grant, ReadExecutionBudget budget)
        {
            var captured = Capture(plan, grant);
            var fence = captured.Authority;
            fences.Add((plan, captured, grant));
            var owner = plan.Partition == Local ? fixture.Source : fixture.Destination;
            var engine = plan.Partition == Local ? source : destination;
            var child = plan with
            {
                MaxReadBytes = grant.RemainingBytes / RowLimit,
                MaxExaminedRecords = grant.RemainingRecords / RowLimit
            };
            child = PartitionQueryParallelRetention.Child(child, NativeSerialization.Measure(
                new PartitionQueryOwnedLeafRequest(child, fence.Destination.Owner, fence.Tenant)));
            return new(actualToken => Task.Run(() =>
            {
                var result = PartitionQueryLeafExecution.Execute(engine, RemoteDocumentNativeFixture.Reader,
                    new(child, fence.Destination.Owner, fence.Tenant), fence.Destination.Owner,
                    (plan.Partition == Local ? sourceClock : destinationClock) ?? owner.Database.EvaluationClock, actualToken);
                return new PartitionQueryOwnedLeaf(fence.Destination.Owner, result);
            }, actualToken), async (actual, actualToken) =>
            {
                actualToken.ThrowIfCancellationRequested();
                budget.ImportReadGrant(grant, actual.Result.ReadBytes, actual.Result.ExaminedRecords);
                completed++;
                if (completed == RemoteDocumentNativeFixture.Version && afterFirstActualLeaf is not null)
                { await afterFirstActualLeaf().ConfigureAwait(false); }
                if (afterActualLeaf is not null)
                { await afterActualLeaf(completed).ConfigureAwait(false); }
            });
        }
    }

    private PartitionQuerySourceFence Capture(PartitionQueryLeafPlanV1 plan, ReadExecutionBudgetReadGrant grant)
        => fixture.Source.Database.WithPartitionQueryFenceView(RemoteDocumentNativeFixture.Reader, plan.Partition,
            RemoteDocumentNativeFixture.Collection, grant, (view, principal, resource, digest) =>
            {
                source.Bind(principal, resource, plan.Request);
                return new PartitionQuerySourceFence(fixture.Source.Database.CaptureRemotePartitionQuery(view, principal, plan.Partition,
                    RemoteDocumentNativeFixture.Collection, grant), digest);
            });

    private Task RevalidateAsync(CancellationToken token)
    {
        foreach (var item in fences)
        {
            token.ThrowIfCancellationRequested();
            var actual = Capture(item.Plan, item.Grant);
            if (!JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(item.Fence)))
            { throw Errors.Fail(ErrorCode.OwnershipLost, AuthorityChangedDetail); }
        }
        return Task.CompletedTask;
    }
}
