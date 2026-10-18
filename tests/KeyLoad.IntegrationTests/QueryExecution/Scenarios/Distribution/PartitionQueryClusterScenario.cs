using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Owns real Orleans seed data and the independent leaf/row oracle.</summary>
internal sealed record PartitionQueryClusterScenario(
    ImmutableArray<PartitionRef> Partitions,
    ImmutableArray<PartitionQueryClusterInputRow> Inputs,
    SelectQuery Query)
{
    private const string Tenant = "pquery-cluster-tenant";
    private const string Domain = "pquery-cluster-domain";
    private const string Database = "pquery-cluster-database";
    private const string GuidFormat = "N";

    internal static async Task<PartitionQueryClusterScenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partitions = CreatePartitions();
        var inputs = CreateInputs(partitions);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await ConfigureResourcesAsync(administrator, partitions, cancellationToken).ConfigureAwait(false);
        await CommitInputsAsync(administrator, inputs, cancellationToken).ConfigureAwait(false);
        return new(partitions, inputs, CreateQuery());
    }

    internal static async Task<PartitionQueryClusterScenario> CreateAuthorizationAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var run = Guid.NewGuid().ToString(GuidFormat);
        var partitions = ImmutableArray.Create(
            new PartitionRef(Tenant, "a-pquery-authorized-db-" + run, Domain, PartitionQueryClusterProtocol.AuthorizedPartition),
            new PartitionRef(Tenant, "b-pquery-denied-db-" + run, Domain, PartitionQueryClusterProtocol.DeniedPartition));
        var inputs = ImmutableArray.Create(
            new PartitionQueryClusterInputRow(partitions[0], "authorized-row", 1, "authorized"),
            new PartitionQueryClusterInputRow(partitions[1], "denied-row", 1, "denied"));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await ConfigureResourcesAsync(administrator, partitions, cancellationToken).ConfigureAwait(false);
        await CommitInputsAsync(administrator, inputs, cancellationToken).ConfigureAwait(false);
        return new(partitions, inputs, CreateQuery());
    }

    internal PartitionQueryRequestV1 Request(ImmutableArray<PartitionRef>? partitions = null,
        SelectQuery? query = null)
        => new(PartitionQueryClusterProtocol.Version, partitions ?? Partitions, query ?? Query,
            Parameters: null, AllowFullScan: true, AstVersion: PartitionQueryClusterProtocol.AstVersion);

    internal ImmutableArray<PartitionQueryClusterInputRow> ExpectedRows(IEnumerable<PartitionRef> selected)
    {
        var selectedSet = selected.ToHashSet();
        return Inputs.Where(row => selectedSet.Contains(row.Partition))
            .OrderBy(row => row.Rank)
            .ThenBy(row => row.Partition.TenantId, StringComparer.Ordinal)
            .ThenBy(row => row.Partition.DatabaseId, StringComparer.Ordinal)
            .ThenBy(row => row.Partition.TransactionDomainId, StringComparer.Ordinal)
            .ThenBy(row => row.Partition.PartitionKey, StringComparer.Ordinal)
            .ThenBy(_ => PartitionQueryClusterProtocol.Collection, StringComparer.Ordinal)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<PartitionRef> CreatePartitions()
    {
        var run = Guid.NewGuid().ToString(GuidFormat);
        return ImmutableArray.Create(
            new PartitionRef(Tenant + "-" + run, Database, Domain, PartitionQueryClusterProtocol.BoundPartition),
            new PartitionRef(Tenant + "-" + run, Database, Domain, PartitionQueryClusterProtocol.SecondPartition),
            new PartitionRef(Tenant + "-" + run, Database, Domain, PartitionQueryClusterProtocol.EmptyPartition));
    }

    private static ImmutableArray<PartitionQueryClusterInputRow> CreateInputs(ImmutableArray<PartitionRef> partitions)
        => ImmutableArray.Create(
            new PartitionQueryClusterInputRow(partitions[0], "first-bound", 1, "bound-first"),
            new PartitionQueryClusterInputRow(partitions[0], PartitionQueryClusterProtocol.DuplicateTextId, 2, "bound-same"),
            new PartitionQueryClusterInputRow(partitions[0], "z-bound", 2, "bound-z"),
            new PartitionQueryClusterInputRow(partitions[1], "first-second", 1, "second-first"),
            new PartitionQueryClusterInputRow(partitions[1], "a-second", 2, "second-a"),
            new PartitionQueryClusterInputRow(partitions[1], PartitionQueryClusterProtocol.DuplicateTextId, 2, "second-same"));

    private static SelectQuery CreateQuery()
        => new(PartitionQueryClusterProtocol.Collection, null,
            [new(PartitionQueryClusterProtocol.ValueField, PartitionQueryClusterProtocol.ValueAlias)],
            null, [new(PartitionQueryClusterProtocol.RankField, Descending: false)],
            PartitionQueryClusterProtocol.Limit);

    private static async Task ConfigureResourcesAsync(KeyLoadClient administrator,
        ImmutableArray<PartitionRef> partitions, CancellationToken cancellationToken)
    {
        foreach (var partition in partitions)
        {
            var definition = new ResourceDefinition(PartitionQueryClusterProtocol.Collection,
                ResourceKind.Collection, partition.TransactionDomainId)
            {
                Indexes = [new(PartitionQueryClusterProtocol.RankIndex, [PartitionQueryClusterProtocol.RankField], Unique: false)]
            };
            await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, definition), cancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);
        }
    }

    private static async Task CommitInputsAsync(KeyLoadClient administrator,
        ImmutableArray<PartitionQueryClusterInputRow> inputs, CancellationToken cancellationToken)
    {
        foreach (var group in inputs.GroupBy(row => row.Partition))
        {
            var puts = group.Select(row => (Mutation)new PutDocument(PartitionQueryClusterProtocol.Collection,
                row.Id, Serialize(row), ExpectedRevision: 0)).ToImmutableArray();
            var command = new CommandRequest(Guid.NewGuid(), group.Key, puts);
            await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command, cancellationToken)
                .ConfigureAwait(false)).ConfigureAwait(false);
        }
    }

    private static string Serialize(PartitionQueryClusterInputRow row)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PartitionQueryClusterProtocol.RankJsonProperty] = row.Rank,
            [PartitionQueryClusterProtocol.ValueAlias] = row.Value
        }, JsonDefaults.Options);
}
