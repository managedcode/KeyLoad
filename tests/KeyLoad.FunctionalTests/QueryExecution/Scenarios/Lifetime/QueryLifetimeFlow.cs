using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.FunctionalTests.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.QueryExecution;

/// <summary>Exercises authorized execution leaf execution against two original real ZoneTree owners.</summary>
internal sealed class QueryLifetimeFlow(QueryFixture fixture, bool sourceNarrower)
{
    internal QueryFixture Fixture => fixture;
    private long sourceEpoch = QueryLifetimeProtocol.SourceEpoch;
    private long targetEpoch = QueryLifetimeProtocol.TargetEpoch;

    internal async Task SeedAsync(bool restrictedRows)
    {
        fixture.Seed();
        Principal(fixture.Source, sourceEpoch, sourceNarrower, restrictedRows && sourceNarrower);
        Principal(fixture.Destination, targetEpoch, !sourceNarrower, restrictedRows && !sourceNarrower);
        await QueryLifetimeAssertions.UnchangedAsync(this, async () =>
        {
            await QueryLifetimeAssertions.RefusedAsync(() => _ = Query(), ErrorCode.PermissionDenied,
                QueryLifetimeProtocol.ReceivingIdentityChanged);
            await QueryLifetimeAssertions.RefusedAsync(() => _ = Statistics(), ErrorCode.PermissionDenied,
                QueryLifetimeProtocol.ReceivingIdentityChanged);
        });
        sourceEpoch = targetEpoch = checked(Math.Max(sourceEpoch, targetEpoch) + QueryLifetimeProtocol.NextEpoch);
        Principal(fixture.Source, sourceEpoch, narrow: true, restrictedRows);
        Principal(fixture.Destination, targetEpoch, narrow: true, restrictedRows);
    }

    internal void RepairRows()
    {
        sourceEpoch += QueryLifetimeProtocol.NextEpoch;
        targetEpoch += QueryLifetimeProtocol.NextEpoch;
        Principal(fixture.Source, sourceEpoch, narrow: true, restrictedRows: false);
        Principal(fixture.Destination, targetEpoch, narrow: true, restrictedRows: false);
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
        var work = new ReadExecutionBudget(database.EvaluationClock, CancellationToken.None);
        var grant = work.CreateReadGrant(work.RemainingReadGrantBytes / QueryLifetimeProtocol.GrantDivisor, work.RemainingReadGrantRecords / QueryLifetimeProtocol.GrantDivisor);
        return database.WithPartitionQueryFenceView<PartitionQueryLifetimeMetadata>(QueryFixture.Reader,
            QueryFixture.Partition, QueryFixture.Collection, grant,
            (view, principal, resource, _) => new(resource,
                ResourceLifetimeCoverageValidation.Capture(work.CreateView(view, grant),
                    QueryFixture.Partition, [resource]), principal));
    }

    internal ResourceLifetimeCoverage CreateOtherLifetime()
    {
        var definition = new ResourceDefinition(QueryLifetimeProtocol.OtherResource, ResourceKind.Collection,
            QueryFixture.Partition.TransactionDomainId);
        _ = fixture.Source.Database.Apply(QueryFixture.Operation(fixture.Source,
            OperationKind.ConfigureResource, Guid.NewGuid(), new ConfigureResourceRequest(QueryFixture.Tenant,
                QueryFixture.Partition.DatabaseId, definition))).Get<ResourceDefinition>();
        return fixture.Source.Store.Read(view => ResourceLifetimeCoverageValidation.Capture(view,
            QueryFixture.Partition, [definition]));
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
        var normalized = QueryValidation.Normalize(request, engine.Execution);
        var plan = PartitionQueryPlanFactory.Create(normalized, database.Store.Identity,
            [request.Partition], engine.Execution).Leaves.Single();
        var input = new PartitionQueryOwnedLeafRequest(plan,
            fixture.DestinationOwner, QueryFixture.Tenant,
            metadata.Definition, metadata.Coverage, metadata.Principal);
        var roundTrip = runtimeRoundTrip
            ? OrleansSerialization.Deserialize<PartitionQueryOwnedLeafRequest>(OrleansSerialization.Serialize(input)) : input;
        return PartitionQueryLifetimeLeafExecution.Execute(engine, QueryFixture.Reader, roundTrip,
            fixture.DestinationOwner, database.EvaluationClock,
            database.EvaluationClock.GetUtcNow() + UnitExecutionOptions.OperationLifetime, cancellationToken);
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
            long.MaxValue / QueryLifetimeProtocol.GrantDivisor,
            int.MaxValue / QueryLifetimeProtocol.GrantDivisor,
            QueryResultBudgetPolicy.Resolve(UnitExecutionOptions.QueryExecution().Value)
                / QueryLifetimeProtocol.GrantDivisor, null, null, null, null, [], metadata.Definition, metadata.Coverage, metadata.Principal);
        var roundTrip = runtimeRoundTrip
            ? OrleansSerialization.Deserialize<DistributedSearchOwnedLeaf>(OrleansSerialization.Serialize(input)) : input;
        return DistributedSearchOwnedLeafExecution.Execute(database, QueryFixture.Reader,
            roundTrip, fixture.DestinationOwner, UnitExecutionOptions.QueryExecution(),
            database.EvaluationClock, database.EvaluationClock.GetUtcNow() + UnitExecutionOptions.OperationLifetime,
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
