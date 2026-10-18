using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Runs bounded native leaf execution and merge over actual independent stores.</summary>
internal sealed class QueryNativePartitionFlow(QueryNativeFixture fixture, TimeProvider? sourceClock = null,
    TimeProvider? destinationClock = null)
{
    internal const string SourceJson = "{\"title\":\"source\",\"secret\":\"source-private-canary\"}";
    internal const string SourceProjected = "{\"title\":\"source\"}";
    internal const long SourceEpoch = 3;
    internal const long DestinationEpoch = 2;
    private const int RowLimit = 2;
    private readonly List<(PartitionQueryLeafPlanV1 Plan, PartitionQueryLifetimeMetadata Metadata,
        ReadExecutionBudgetReadGrant Grant)> captured = [];
    internal static PartitionRef Local => QueryNativeFixture.Partition with { PartitionKey = "source" };

    internal void Seed()
    {
        fixture.Seed();
        SetSourceGrant(DestinationEpoch, true);
        SetSourceGrant(SourceEpoch, true);
        var id = Guid.NewGuid();
        _ = fixture.Source.Submit(QueryNativeFixture.Operation(fixture.Source, OperationKind.Batch, id,
            new CommandRequest(id, Local, [new PutDocument(QueryNativeFixture.Collection, QueryNativeFixture.DocumentId,
                SourceJson)]))).Get<CommitReceipt>();
    }

    internal void GrantDestination() => QueryNativeFixture.Principal(fixture.Destination,
        Principal(DestinationEpoch, true));

    internal void SetSourceGrant(long epoch, bool granted) => QueryNativeFixture.Principal(fixture.Source,
        Principal(epoch, granted));

    private static PrincipalRecord Principal(long epoch, bool granted) => new(QueryNativeFixture.Reader,
        QueryNativeFixture.Tenant, granted ? [new(Local.DatabaseId, QueryNativeFixture.Collection,
            Capability.Query | Capability.DocumentsRead)] : [], []) { PolicyEpoch = epoch };

    internal Task<PartitionQueryPageV1> ExecuteAsync(CancellationToken token)
    {
        captured.Clear();
        var request = new PartitionQueryRequestV1(QueryNativeFixture.Version, [Local, QueryNativeFixture.Partition],
            new(QueryNativeFixture.Collection, null, [new(QueryNativeFixture.Title, "title")], null,
                [new(QueryNativeFixture.Title, false)], RowLimit), null, true, QueryNativeFixture.Version);
        var engine = new QueryEngine(fixture.Source.Database, UnitExecutionOptions.QueryExecution());
        return PartitionQueryRemoteExecution.ExecuteAsync(engine, request, Prepare, RevalidateAsync,
            fixture.Source.Database.EvaluationClock, token);
    }

    private PartitionQueryPreparedLeaf Prepare(PartitionQueryLeafPlanV1 plan, ReadExecutionBudgetReadGrant grant,
        ReadExecutionBudget budget)
    {
        var metadata = Capture(plan, grant);
        captured.Add((plan, metadata, grant));
        var owner = plan.Partition == Local ? fixture.Source : fixture.Destination;
        var identity = plan.Partition == Local ? fixture.SourceOwner : fixture.DestinationOwner;
        var clock = (plan.Partition == Local ? sourceClock : destinationClock) ?? owner.Database.EvaluationClock;
        var expiry = owner.Database.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(owner.Database.Limits.QueryDeadlineSeconds);
        var child = plan with
        {
            MaxReadBytes = grant.RemainingBytes / RowLimit,
            MaxExaminedRecords = grant.RemainingRecords / RowLimit
        };
        var request = new PartitionQueryOwnedLeafRequestV2(new(child, identity, QueryNativeFixture.Tenant),
            metadata.Definition, metadata.Coverage, metadata.Principal);
        child = PartitionQueryParallelRetention.Child(child, NativeSerialization.Measure(request));
        request = request with { Original = request.Original with { Plan = child } };
        var roundTrip = NativeSerialization.Deserialize<PartitionQueryOwnedLeafRequestV2>(NativeSerialization.Serialize(request));
        var engine = new QueryEngine(owner.Database, UnitExecutionOptions.QueryExecution());
        return new(actualToken => Task.FromResult(new PartitionQueryOwnedLeaf(identity,
            PartitionQueryLifetimeLeafExecution.Execute(engine, QueryNativeFixture.Reader, roundTrip, identity, clock,
                expiry, actualToken))), (actual, actualToken) =>
        {
            actualToken.ThrowIfCancellationRequested();
            budget.ImportReadGrant(grant, actual.Result.ReadBytes, actual.Result.ExaminedRecords);
            return Task.CompletedTask;
        });
    }

    private PartitionQueryLifetimeMetadata Capture(PartitionQueryLeafPlanV1 plan, ReadExecutionBudgetReadGrant grant)
        => fixture.Source.Database.WithPartitionQueryFenceView(QueryNativeFixture.Reader, plan.Partition,
            QueryNativeFixture.Collection, grant, (view, principal, resource, _) => new(resource,
                PartitionControlLifetimeCoverage.Capture(grant.CreateView(view), plan.Partition, [resource],
                    fixture.Source.Database.Limits.MaxBatchBytes), principal));

    private Task RevalidateAsync(CancellationToken token)
    {
        foreach (var item in captured)
        {
            token.ThrowIfCancellationRequested();
            var current = Capture(item.Plan, item.Grant);
            if (!NativeSerialization.Serialize(current).AsSpan().SequenceEqual(NativeSerialization.Serialize(item.Metadata)))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, "The query source authority changed.");
            }
        }
        return Task.CompletedTask;
    }
}
