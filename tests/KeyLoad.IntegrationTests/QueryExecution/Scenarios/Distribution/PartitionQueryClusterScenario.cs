using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Query;
using KeyLoad.Server;
using ManagedCode.Communication.Commands;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Owns real Orleans seed data and the independent leaf/row oracle.</summary>
internal sealed record PartitionQueryClusterScenario(
    ImmutableArray<PartitionRef> Partitions,
    ImmutableArray<PartitionQueryClusterInputRow> Inputs,
    SelectQuery Query,
    ImmutableDictionary<(string TenantId, string DatabaseId, string ResourceName), EntityRefLifetime> ResourceLifetimes)
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
        var lifetimes = await ConfigureResourcesAsync(administrator, partitions, cancellationToken).ConfigureAwait(false);
        await CommitInputsAsync(administrator, inputs, cancellationToken).ConfigureAwait(false);
        return new(partitions, inputs, CreateQuery(), lifetimes);
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
        var lifetimes = await ConfigureResourcesAsync(administrator, partitions, cancellationToken).ConfigureAwait(false);
        await CommitInputsAsync(administrator, inputs, cancellationToken).ConfigureAwait(false);
        return new(partitions, inputs, CreateQuery(), lifetimes);
    }

    internal PartitionQueryRequest Request(ImmutableArray<PartitionRef>? partitions = null,
        SelectQuery? query = null)
        => new(PartitionQueryClusterProtocol.Version, partitions ?? Partitions, query ?? Query,
            Parameters: null, AllowFullScan: true, AstVersion: PartitionQueryClusterProtocol.AstVersion);

    internal EntityRef ExpectedReference(PartitionQueryClusterInputRow row)
        => new(row.Partition, PartitionQueryClusterProtocol.Collection, row.Id)
        {
            Lifetime = ResourceLifetimes[(row.Partition.TenantId, row.Partition.DatabaseId,
                PartitionQueryClusterProtocol.Collection)]
        };

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

    private static async Task<ImmutableDictionary<(string TenantId, string DatabaseId, string ResourceName), EntityRefLifetime>> ConfigureResourcesAsync(KeyLoadClient administrator,
        ImmutableArray<PartitionRef> partitions, CancellationToken cancellationToken)
    {
        var lifetimes = ImmutableDictionary.CreateBuilder<(string TenantId, string DatabaseId, string ResourceName), EntityRefLifetime>();
        foreach (var partition in partitions)
        {
            var definition = new ResourceDefinition(PartitionQueryClusterProtocol.Collection,
                ResourceKind.Collection, partition.TransactionDomainId)
            {
                Indexes = [new(PartitionQueryClusterProtocol.RankIndex, [PartitionQueryClusterProtocol.RankField], Unique: false)]
            };
            var commandId = Guid.NewGuid();
            var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
            await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(commandId,
                request, cancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);
            var scope = (request.TenantId, request.DatabaseId, definition.Name);
            if (!lifetimes.ContainsKey(scope))
            {
                lifetimes.Add(scope, ExpectedLifetime(commandId, request));
            }
        }
        return lifetimes.ToImmutable();
    }

    private static EntityRefLifetime ExpectedLifetime(Guid commandId, ConfigureResourceRequest request)
    {
        var principalId = PartitionStoreProtocol.AdministratorId;
        var fingerprint = DatabaseCommandFingerprint.Compute(commandId, OperationKind.ConfigureResource,
            principalId, JsonSerializer.Serialize(request, JsonDefaults.Options));
        var original = Command<DatabaseCommand>.Create(nameof(DatabaseCommand),
            new DatabaseCommand(OperationKind.ConfigureResource, OrleansSerialization.Serialize(request), fingerprint), commandId);
        original.UserId = principalId;
        var lifetime = ResourceLifetimeIdentity.Create(original, request.TenantId, request.DatabaseId, request.Definition);
        return new(lifetime.Incarnation.Value);
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
