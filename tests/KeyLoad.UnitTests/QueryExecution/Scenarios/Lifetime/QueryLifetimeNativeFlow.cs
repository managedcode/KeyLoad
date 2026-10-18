using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Uses two original real ZoneTree owners; this is target-execution evidence, not an CLUSTER transport.</summary>
internal sealed class QueryLifetimeNativeFlow(QueryNativeFixture fixture, bool sourceNarrower)
{
    internal QueryNativeFixture Fixture => fixture;
    private long sourceEpoch = QueryLifetimeNativeProtocol.SourceEpoch;
    private long targetEpoch = QueryLifetimeNativeProtocol.TargetEpoch;

    internal void Seed(bool restrictedRows)
    {
        fixture.Seed();
        Principal(fixture.Source, sourceEpoch, sourceNarrower, restrictedRows && sourceNarrower);
        Principal(fixture.Destination, targetEpoch, !sourceNarrower, restrictedRows && !sourceNarrower);
    }

    internal void RepairRows()
    {
        sourceEpoch += QueryLifetimeNativeProtocol.NextEpoch;
        targetEpoch += QueryLifetimeNativeProtocol.NextEpoch;
        Principal(fixture.Source, sourceEpoch, sourceNarrower, restrictedRows: false);
        Principal(fixture.Destination, targetEpoch, !sourceNarrower, restrictedRows: false);
    }

    internal void HideNarrowSchema() => SetSchema(hidden: true);
    internal void RepairSchema() => SetSchema(hidden: false);

    private void SetSchema(bool hidden)
    {
        var owner = sourceNarrower ? fixture.Source : fixture.Destination;
        var current = owner.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            QueryNativeFixture.Tenant, QueryNativeFixture.Partition.DatabaseId,
            QueryNativeFixture.Collection))) ?? throw new InvalidOperationException(QueryLifetimeNativeProtocol.MissingFailure);
        var changed = current with
        {
            SchemaVersion = current.SchemaVersion + QueryLifetimeNativeProtocol.NextEpoch,
            FieldPolicies = [new(QueryNativeFixture.Secret, QueryNativeFixture.Classification,
                SchemaReadGrant: hidden ? QueryLifetimeNativeProtocol.SchemaGrant : null)]
        };
        var request = new ConfigureResourceRequest(QueryNativeFixture.Tenant,
            QueryNativeFixture.Partition.DatabaseId, changed)
        {
            ExpectedSchemaVersion = current.SchemaVersion
        };
        _ = owner.Database.Apply(QueryNativeFixture.Operation(owner, OperationKind.ConfigureResource,
            Guid.NewGuid(), request)).Get<ResourceDefinition>();
    }

    internal PartitionQueryLifetimeMetadata Capture()
    {
        var database = fixture.Source.Database;
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, CancellationToken.None);
        var grant = work.CreateReadGrant(database.Limits.MaxQueryReadBytes / QueryLifetimeNativeProtocol.GrantDivisor,
            database.Limits.MaxScanRecords / QueryLifetimeNativeProtocol.GrantDivisor);
        return database.WithPartitionQueryFenceView<PartitionQueryLifetimeMetadata>(QueryNativeFixture.Reader,
            QueryNativeFixture.Partition, QueryNativeFixture.Collection, grant,
            (view, principal, resource, _) => new(resource,
                ResourceLifetimeCoverage.Capture(work.CreateView(view, grant),
                    QueryNativeFixture.Partition, [resource], database.Limits.MaxBatchBytes), principal));
    }

    internal ResourceLifetimeCoverageV1 CreateOtherLifetime()
    {
        var definition = new ResourceDefinition(QueryLifetimeNativeProtocol.OtherResource, ResourceKind.Collection,
            QueryNativeFixture.Partition.TransactionDomainId);
        _ = fixture.Source.Database.Apply(QueryNativeFixture.Operation(fixture.Source,
            OperationKind.ConfigureResource, Guid.NewGuid(), new ConfigureResourceRequest(QueryNativeFixture.Tenant,
                QueryNativeFixture.Partition.DatabaseId, definition))).Get<ResourceDefinition>();
        return fixture.Source.Store.Read(view => ResourceLifetimeCoverage.Capture(view,
            QueryNativeFixture.Partition, [definition], fixture.Source.Database.Limits.MaxBatchBytes));
    }

    internal PartitionQueryLeafResultV1 Query(string path = QueryLifetimeNativeProtocol.Star,
        PartitionQueryLifetimeMetadata? captured = null, Predicate? filter = null, bool nativeRoundTrip = true)
    {
        var metadata = captured ?? Capture();
        var database = fixture.Destination.Database;
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var request = new AstQueryRequest(QueryNativeFixture.Partition,
            new(QueryNativeFixture.Collection, null, [new(path, QueryLifetimeNativeProtocol.SecretAlias)],
                filter, [], QueryLifetimeNativeProtocol.Limit), AllowFullScan: true);
        var normalized = QueryValidation.Normalize(request, database.Limits, engine.Execution);
        var plan = PartitionQueryPlanFactory.Create(normalized, database.Store.Identity,
            [request.Partition], database.Limits, engine.Execution).Leaves.Single();
        var input = new PartitionQueryOwnedLeafRequestV2(new(plan,
            fixture.DestinationOwner, QueryNativeFixture.Tenant),
            metadata.Definition, metadata.Coverage, metadata.Principal);
        var roundTrip = nativeRoundTrip
            ? NativeSerialization.Deserialize<PartitionQueryOwnedLeafRequestV2>(NativeSerialization.Serialize(input)) : input;
        return PartitionQueryLifetimeLeafExecution.Execute(engine, QueryNativeFixture.Reader, roundTrip,
            fixture.DestinationOwner, database.EvaluationClock,
            database.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(database.Limits.QueryDeadlineSeconds), CancellationToken.None);
    }

    internal DistributedSearchLeafResultV1 Statistics(PartitionQueryLifetimeMetadata? captured = null,
        string field = QueryNativeFixture.Title, string text = QueryLifetimeNativeProtocol.Text,
        bool nativeRoundTrip = true)
    {
        var metadata = captured ?? Capture();
        var database = fixture.Destination.Database;
        var original = new DistributedSearchOwnedLeafV1(QueryLifetimeNativeProtocol.Version,
            DistributedSearchPhase.Statistics, new(QueryNativeFixture.Partition,
                QueryNativeFixture.Collection, TextField: field,
                Text: text, Limit: QueryLifetimeNativeProtocol.Limit),
            fixture.DestinationOwner, QueryNativeFixture.Tenant,
            database.Limits.MaxQueryReadBytes / QueryLifetimeNativeProtocol.GrantDivisor,
            database.Limits.MaxScanRecords / QueryLifetimeNativeProtocol.GrantDivisor,
            QueryResultBudgetPolicy.Resolve(database.Limits, UnitExecutionOptions.QueryExecution().Value)
                / QueryLifetimeNativeProtocol.GrantDivisor, null, null, null, null, []);
        var input = new DistributedSearchOwnedLeafV2(original, metadata.Definition, metadata.Coverage, metadata.Principal);
        var roundTrip = nativeRoundTrip
            ? NativeSerialization.Deserialize<DistributedSearchOwnedLeafV2>(NativeSerialization.Serialize(input)) : input;
        return DistributedSearchOwnedLeafExecution.ExecuteV2(database, QueryNativeFixture.Reader,
            roundTrip, fixture.DestinationOwner, UnitExecutionOptions.QueryExecution(),
            database.EvaluationClock, database.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(database.Limits.QueryDeadlineSeconds),
            CancellationToken.None);
    }

    private static void Principal(DocumentPatchAtomicityOwner owner, long epoch, bool narrow, bool restrictedRows)
    {
        var principal = new PrincipalRecord(QueryNativeFixture.Reader, QueryNativeFixture.Tenant,
            [new(QueryNativeFixture.Partition.DatabaseId, QueryNativeFixture.Collection,
                Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)], narrow ? [] : [QueryLifetimeNativeProtocol.Star])
        {
            PolicyEpoch = epoch,
            RestrictRows = restrictedRows
        };
        _ = owner.Database.Apply(QueryNativeFixture.Operation(owner, OperationKind.ConfigurePrincipal,
            Guid.NewGuid(), new ConfigurePrincipalRequest(principal))).Get<PrincipalRecord>();
    }
}
