using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Exercises authorized execution leaf execution against two original real ZoneTree owners.</summary>
internal sealed class QueryLifetimeFlow(QueryFixture fixture, bool sourceNarrower)
{
    internal QueryFixture Fixture => fixture;
    private long sourceEpoch = QueryLifetimeProtocol.SourceEpoch;
    private long targetEpoch = QueryLifetimeProtocol.TargetEpoch;

    internal void Seed(bool restrictedRows)
    {
        fixture.Seed();
        Principal(fixture.Source, sourceEpoch, sourceNarrower, restrictedRows && sourceNarrower);
        Principal(fixture.Destination, targetEpoch, !sourceNarrower, restrictedRows && !sourceNarrower);
    }

    internal void RepairRows()
    {
        sourceEpoch += QueryLifetimeProtocol.NextEpoch;
        targetEpoch += QueryLifetimeProtocol.NextEpoch;
        Principal(fixture.Source, sourceEpoch, sourceNarrower, restrictedRows: false);
        Principal(fixture.Destination, targetEpoch, !sourceNarrower, restrictedRows: false);
    }

    internal void HideNarrowSchema() => SetSchema(hidden: true);
    internal void RepairSchema() => SetSchema(hidden: false);

    private void SetSchema(bool hidden)
    {
        var owner = sourceNarrower ? fixture.Source : fixture.Destination;
        var current = owner.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            QueryFixture.Tenant, QueryFixture.Partition.DatabaseId,
            QueryFixture.Collection))) ?? throw new InvalidOperationException(QueryLifetimeProtocol.MissingFailure);
        var changed = current with
        {
            SchemaVersion = current.SchemaVersion + QueryLifetimeProtocol.NextEpoch,
            FieldPolicies = [new(QueryFixture.Secret, QueryFixture.Classification,
                SchemaReadGrant: hidden ? QueryLifetimeProtocol.SchemaGrant : null)]
        };
        var request = new ConfigureResourceRequest(QueryFixture.Tenant,
            QueryFixture.Partition.DatabaseId, changed)
        {
            ExpectedSchemaVersion = current.SchemaVersion
        };
        _ = owner.Database.Apply(QueryFixture.Operation(owner, OperationKind.ConfigureResource,
            Guid.NewGuid(), request)).Get<ResourceDefinition>();
    }

    internal PartitionQueryLifetimeMetadata Capture()
    {
        var database = fixture.Source.Database;
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, CancellationToken.None);
        var grant = work.CreateReadGrant(database.Limits.MaxQueryReadBytes / QueryLifetimeProtocol.GrantDivisor,
            database.Limits.MaxScanRecords / QueryLifetimeProtocol.GrantDivisor);
        return database.WithPartitionQueryFenceView<PartitionQueryLifetimeMetadata>(QueryFixture.Reader,
            QueryFixture.Partition, QueryFixture.Collection, grant,
            (view, principal, resource, _) => new(resource,
                ResourceLifetimeCoverageValidation.Capture(work.CreateView(view, grant),
                    QueryFixture.Partition, [resource], database.Limits.MaxBatchBytes), principal));
    }

    internal ResourceLifetimeCoverage CreateOtherLifetime()
    {
        var definition = new ResourceDefinition(QueryLifetimeProtocol.OtherResource, ResourceKind.Collection,
            QueryFixture.Partition.TransactionDomainId);
        _ = fixture.Source.Database.Apply(QueryFixture.Operation(fixture.Source,
            OperationKind.ConfigureResource, Guid.NewGuid(), new ConfigureResourceRequest(QueryFixture.Tenant,
                QueryFixture.Partition.DatabaseId, definition))).Get<ResourceDefinition>();
        return fixture.Source.Store.Read(view => ResourceLifetimeCoverageValidation.Capture(view,
            QueryFixture.Partition, [definition], fixture.Source.Database.Limits.MaxBatchBytes));
    }

    internal PartitionQueryLeafResult Query(string path = QueryLifetimeProtocol.Star,
        PartitionQueryLifetimeMetadata? captured = null, Predicate? filter = null, bool runtimeRoundTrip = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = captured ?? Capture();
        var database = fixture.Destination.Database;
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var request = new AstQueryRequest(QueryFixture.Partition,
            new(QueryFixture.Collection, null, [new(path, QueryLifetimeProtocol.SecretAlias)],
                filter, [], QueryLifetimeProtocol.Limit), AllowFullScan: true);
        var normalized = QueryValidation.Normalize(request, database.Limits, engine.Execution);
        var plan = PartitionQueryPlanFactory.Create(normalized, database.Store.Identity,
            [request.Partition], database.Limits, engine.Execution).Leaves.Single();
        var input = new PartitionQueryOwnedLeafRequest(plan,
            fixture.DestinationOwner, QueryFixture.Tenant,
            metadata.Definition, metadata.Coverage, metadata.Principal);
        var roundTrip = runtimeRoundTrip
            ? OrleansSerialization.Deserialize<PartitionQueryOwnedLeafRequest>(OrleansSerialization.Serialize(input)) : input;
        return PartitionQueryLifetimeLeafExecution.Execute(engine, QueryFixture.Reader, roundTrip,
            fixture.DestinationOwner, database.EvaluationClock,
            database.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(database.Limits.QueryDeadlineSeconds), cancellationToken);
    }

    internal DistributedSearchLeafResult Statistics(PartitionQueryLifetimeMetadata? captured = null,
        string field = QueryFixture.Title, string text = QueryLifetimeProtocol.Text,
        bool runtimeRoundTrip = true)
    {
        var metadata = captured ?? Capture();
        var database = fixture.Destination.Database;
        var input = new DistributedSearchOwnedLeaf(QueryLifetimeProtocol.Version,
            DistributedSearchPhase.Statistics, new(QueryFixture.Partition,
                QueryFixture.Collection, TextField: field,
                Text: text, Limit: QueryLifetimeProtocol.Limit),
            fixture.DestinationOwner, QueryFixture.Tenant,
            database.Limits.MaxQueryReadBytes / QueryLifetimeProtocol.GrantDivisor,
            database.Limits.MaxScanRecords / QueryLifetimeProtocol.GrantDivisor,
            QueryResultBudgetPolicy.Resolve(database.Limits, UnitExecutionOptions.QueryExecution().Value)
                / QueryLifetimeProtocol.GrantDivisor, null, null, null, null, [], metadata.Definition, metadata.Coverage, metadata.Principal);
        var roundTrip = runtimeRoundTrip
            ? OrleansSerialization.Deserialize<DistributedSearchOwnedLeaf>(OrleansSerialization.Serialize(input)) : input;
        return DistributedSearchOwnedLeafExecution.Execute(database, QueryFixture.Reader,
            roundTrip, fixture.DestinationOwner, UnitExecutionOptions.QueryExecution(),
            database.EvaluationClock, database.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(database.Limits.QueryDeadlineSeconds),
            CancellationToken.None);
    }

    private static void Principal(DocumentPatchAtomicityOwner owner, long epoch, bool narrow, bool restrictedRows)
    {
        var principal = new PrincipalRecord(QueryFixture.Reader, QueryFixture.Tenant,
            [new(QueryFixture.Partition.DatabaseId, QueryFixture.Collection,
                Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)], narrow ? [] : [QueryLifetimeProtocol.Star])
        {
            PolicyEpoch = epoch,
            RestrictRows = restrictedRows
        };
        _ = owner.Database.Apply(QueryFixture.Operation(owner, OperationKind.ConfigurePrincipal,
            Guid.NewGuid(), new ConfigurePrincipalRequest(principal))).Get<PrincipalRecord>();
    }
}
