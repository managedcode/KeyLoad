using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Uses two original real ZoneTree owners; this is target-execution evidence, not an RF3 transport.</summary>
internal sealed class QueryLifetimeNativeFlow(RemoteDocumentNativeFixture fixture, bool sourceNarrower)
{
    internal RemoteDocumentNativeFixture Fixture => fixture;
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
            RemoteDocumentNativeFixture.Tenant, RemoteDocumentNativeFixture.Partition.DatabaseId,
            RemoteDocumentNativeFixture.Collection))) ?? throw new InvalidOperationException(QueryLifetimeNativeProtocol.MissingFailure);
        var changed = current with
        {
            SchemaVersion = current.SchemaVersion + QueryLifetimeNativeProtocol.NextEpoch,
            FieldPolicies = [new(RemoteDocumentNativeFixture.Secret, RemoteDocumentNativeFixture.Classification,
                SchemaReadGrant: hidden ? QueryLifetimeNativeProtocol.SchemaGrant : null)]
        };
        var request = new ConfigureResourceRequest(RemoteDocumentNativeFixture.Tenant,
            RemoteDocumentNativeFixture.Partition.DatabaseId, changed)
        {
            ExpectedSchemaVersion = current.SchemaVersion
        };
        _ = owner.Database.Apply(RemoteDocumentNativeFixture.Operation(owner, OperationKind.ConfigureResource,
            Guid.NewGuid(), request)).Get<ResourceDefinition>();
    }

    internal PartitionQueryLifetimeMetadata Capture()
    {
        var database = fixture.Source.Database;
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, CancellationToken.None);
        var grant = work.CreateReadGrant(database.Limits.MaxQueryReadBytes / QueryLifetimeNativeProtocol.GrantDivisor,
            database.Limits.MaxScanRecords / QueryLifetimeNativeProtocol.GrantDivisor);
        return database.WithPartitionQueryFenceView<PartitionQueryLifetimeMetadata>(RemoteDocumentNativeFixture.Reader,
            RemoteDocumentNativeFixture.Partition, RemoteDocumentNativeFixture.Collection, grant,
            (view, principal, resource, _) => new(resource,
                PartitionControlLifetimeCoverage.Capture(work.CreateView(view, grant),
                    RemoteDocumentNativeFixture.Partition, [resource], database.Limits.MaxBatchBytes), principal));
    }

    internal ResourceLifetimeCoverageV1 CreateOtherLifetime()
    {
        var definition = new ResourceDefinition(QueryLifetimeNativeProtocol.OtherResource, ResourceKind.Collection,
            RemoteDocumentNativeFixture.Partition.TransactionDomainId);
        _ = fixture.Source.Database.Apply(RemoteDocumentNativeFixture.Operation(fixture.Source,
            OperationKind.ConfigureResource, Guid.NewGuid(), new ConfigureResourceRequest(RemoteDocumentNativeFixture.Tenant,
                RemoteDocumentNativeFixture.Partition.DatabaseId, definition))).Get<ResourceDefinition>();
        return fixture.Source.Store.Read(view => PartitionControlLifetimeCoverage.Capture(view,
            RemoteDocumentNativeFixture.Partition, [definition], fixture.Source.Database.Limits.MaxBatchBytes));
    }

    internal PartitionQueryLeafResultV1 Query(string path = QueryLifetimeNativeProtocol.Star,
        PartitionQueryLifetimeMetadata? captured = null, Predicate? filter = null, bool nativeRoundTrip = true)
    {
        var metadata = captured ?? Capture();
        var database = fixture.Destination.Database;
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var request = new AstQueryRequest(RemoteDocumentNativeFixture.Partition,
            new(RemoteDocumentNativeFixture.Collection, null, [new(path, QueryLifetimeNativeProtocol.SecretAlias)],
                filter, [], QueryLifetimeNativeProtocol.Limit), AllowFullScan: true);
        var normalized = QueryValidation.Normalize(request, database.Limits, engine.Execution);
        var plan = PartitionQueryPlanFactory.Create(normalized, database.Store.Identity,
            [request.Partition], database.Limits, engine.Execution).Leaves.Single();
        var input = new PartitionQueryOwnedLeafRequestV2(new(plan,
            PhysicalOwnerDirectoryWholeFlow.Destination.Owner, RemoteDocumentNativeFixture.Tenant),
            metadata.Definition, metadata.Coverage, metadata.Principal);
        var roundTrip = nativeRoundTrip
            ? NativeSerialization.Deserialize<PartitionQueryOwnedLeafRequestV2>(NativeSerialization.Serialize(input)) : input;
        return PartitionQueryLifetimeLeafExecution.Execute(engine, RemoteDocumentNativeFixture.Reader, roundTrip,
            PhysicalOwnerDirectoryWholeFlow.Destination.Owner, database.EvaluationClock,
            database.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(database.Limits.QueryDeadlineSeconds), CancellationToken.None);
    }

    internal DistributedSearchLeafResultV1 Statistics(PartitionQueryLifetimeMetadata? captured = null,
        string field = RemoteDocumentNativeFixture.Title, string text = QueryLifetimeNativeProtocol.Text,
        bool nativeRoundTrip = true)
    {
        var metadata = captured ?? Capture();
        var database = fixture.Destination.Database;
        var original = new DistributedSearchOwnedLeafV1(QueryLifetimeNativeProtocol.Version,
            DistributedSearchPhase.Statistics, new(RemoteDocumentNativeFixture.Partition,
                RemoteDocumentNativeFixture.Collection, TextField: field,
                Text: text, Limit: QueryLifetimeNativeProtocol.Limit),
            PhysicalOwnerDirectoryWholeFlow.Destination.Owner, RemoteDocumentNativeFixture.Tenant,
            database.Limits.MaxQueryReadBytes, database.Limits.MaxScanRecords,
            QueryResultBudgetPolicy.Resolve(database.Limits, UnitExecutionOptions.QueryExecution().Value)
                / QueryLifetimeNativeProtocol.GrantDivisor, null, null, null, null, default);
        var input = new DistributedSearchOwnedLeafV2(original, metadata.Definition, metadata.Coverage, metadata.Principal);
        var roundTrip = nativeRoundTrip
            ? NativeSerialization.Deserialize<DistributedSearchOwnedLeafV2>(NativeSerialization.Serialize(input)) : input;
        return DistributedSearchOwnedLeafExecution.ExecuteV2(database, RemoteDocumentNativeFixture.Reader,
            roundTrip, PhysicalOwnerDirectoryWholeFlow.Destination.Owner, UnitExecutionOptions.QueryExecution(),
            database.EvaluationClock, database.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(database.Limits.QueryDeadlineSeconds),
            CancellationToken.None);
    }

    private static void Principal(PhysicalShardCatalogFixture owner, long epoch, bool narrow, bool restrictedRows)
    {
        var principal = new PrincipalRecord(RemoteDocumentNativeFixture.Reader, RemoteDocumentNativeFixture.Tenant,
            [new(RemoteDocumentNativeFixture.Partition.DatabaseId, RemoteDocumentNativeFixture.Collection,
                Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)], narrow ? [] : [QueryLifetimeNativeProtocol.Star])
        {
            PolicyEpoch = epoch,
            RestrictRows = restrictedRows
        };
        _ = owner.Database.Apply(RemoteDocumentNativeFixture.Operation(owner, OperationKind.ConfigurePrincipal,
            Guid.NewGuid(), new ConfigurePrincipalRequest(principal))).Get<PrincipalRecord>();
    }
}
