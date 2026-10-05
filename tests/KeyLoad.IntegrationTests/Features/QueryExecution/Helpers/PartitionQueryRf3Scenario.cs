using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Owns real RF3 seed data and the independent leaf/row oracle.</summary>
internal sealed record PartitionQueryRf3Scenario(
    ImmutableArray<PartitionRef> Partitions,
    ImmutableArray<PartitionQueryRf3InputRow> Inputs,
    SelectQuery Query)
{
    private const string Tenant = "pquery-rf3-tenant";
    private const string Domain = "pquery-rf3-domain";
    private const string Database = "pquery-rf3-database";
    private const string GuidFormat = "N";

    internal static async Task<PartitionQueryRf3Scenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partitions = CreatePartitions();
        var inputs = CreateInputs(partitions);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await ConfigureResourcesAsync(administrator, partitions, cancellationToken).ConfigureAwait(false);
        await CommitInputsAsync(administrator, inputs, cancellationToken).ConfigureAwait(false);
        await BindFirstPartitionAsync(administrator, fixture, partitions[0], cancellationToken).ConfigureAwait(false);
        await VerifyBoundAndFallbackAsync(administrator, fixture, partitions[0], partitions[1], cancellationToken)
            .ConfigureAwait(false);
        return new(partitions, inputs, CreateQuery());
    }

    internal static async Task<PartitionQueryRf3Scenario> CreateAuthorizationAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var run = Guid.NewGuid().ToString(GuidFormat);
        var partitions = ImmutableArray.Create(
            new PartitionRef(Tenant, "a-pquery-authorized-db-" + run, Domain, PartitionQueryRf3Protocol.AuthorizedPartition),
            new PartitionRef(Tenant, "b-pquery-denied-db-" + run, Domain, PartitionQueryRf3Protocol.DeniedPartition));
        var inputs = ImmutableArray.Create(
            new PartitionQueryRf3InputRow(partitions[0], "authorized-row", 1, "authorized"),
            new PartitionQueryRf3InputRow(partitions[1], "denied-row", 1, "denied"));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await ConfigureResourcesAsync(administrator, partitions, cancellationToken).ConfigureAwait(false);
        await CommitInputsAsync(administrator, inputs, cancellationToken).ConfigureAwait(false);
        return new(partitions, inputs, CreateQuery());
    }

    internal PartitionQueryRequestV1 Request(ImmutableArray<PartitionRef>? partitions = null,
        SelectQuery? query = null)
        => new(PartitionQueryRf3Protocol.Version, partitions ?? Partitions, query ?? Query,
            Parameters: null, AllowFullScan: true, AstVersion: PartitionQueryRf3Protocol.AstVersion);

    internal ImmutableArray<PartitionQueryRf3InputRow> ExpectedRows(IEnumerable<PartitionRef> selected)
    {
        var selectedSet = selected.ToHashSet();
        return Inputs.Where(row => selectedSet.Contains(row.Partition))
            .OrderBy(row => row.Rank)
            .ThenBy(row => row.Partition.TenantId, StringComparer.Ordinal)
            .ThenBy(row => row.Partition.DatabaseId, StringComparer.Ordinal)
            .ThenBy(row => row.Partition.TransactionDomainId, StringComparer.Ordinal)
            .ThenBy(row => row.Partition.PartitionKey, StringComparer.Ordinal)
            .ThenBy(_ => PartitionQueryRf3Protocol.Collection, StringComparer.Ordinal)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<PartitionRef> CreatePartitions()
    {
        var run = Guid.NewGuid().ToString(GuidFormat);
        return ImmutableArray.Create(
            new PartitionRef(Tenant + "-" + run, Database, Domain, PartitionQueryRf3Protocol.BoundPartition),
            new PartitionRef(Tenant + "-" + run, Database, Domain, PartitionQueryRf3Protocol.FallbackPartition),
            new PartitionRef(Tenant + "-" + run, Database, Domain, PartitionQueryRf3Protocol.EmptyPartition));
    }

    private static ImmutableArray<PartitionQueryRf3InputRow> CreateInputs(ImmutableArray<PartitionRef> partitions)
        => ImmutableArray.Create(
            new PartitionQueryRf3InputRow(partitions[0], "first-bound", 1, "bound-first"),
            new PartitionQueryRf3InputRow(partitions[0], PartitionQueryRf3Protocol.DuplicateTextId, 2, "bound-same"),
            new PartitionQueryRf3InputRow(partitions[0], "z-bound", 2, "bound-z"),
            new PartitionQueryRf3InputRow(partitions[1], "first-fallback", 1, "fallback-first"),
            new PartitionQueryRf3InputRow(partitions[1], "a-fallback", 2, "fallback-a"),
            new PartitionQueryRf3InputRow(partitions[1], PartitionQueryRf3Protocol.DuplicateTextId, 2, "fallback-same"));

    private static SelectQuery CreateQuery()
        => new(PartitionQueryRf3Protocol.Collection, null,
            [new(PartitionQueryRf3Protocol.ValueField, PartitionQueryRf3Protocol.ValueAlias)],
            null, [new(PartitionQueryRf3Protocol.RankField, Descending: false)],
            PartitionQueryRf3Protocol.Limit);

    private static async Task ConfigureResourcesAsync(KeyLoadClient administrator,
        ImmutableArray<PartitionRef> partitions, CancellationToken cancellationToken)
    {
        foreach (var partition in partitions)
        {
            var definition = new ResourceDefinition(PartitionQueryRf3Protocol.Collection,
                ResourceKind.Collection, partition.TransactionDomainId)
            { Indexes = [new(PartitionQueryRf3Protocol.RankIndex, [PartitionQueryRf3Protocol.RankField], Unique: false)] };
            await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, definition), cancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);
        }
    }

    private static async Task CommitInputsAsync(KeyLoadClient administrator,
        ImmutableArray<PartitionQueryRf3InputRow> inputs, CancellationToken cancellationToken)
    {
        foreach (var group in inputs.GroupBy(row => row.Partition))
        {
            var puts = group.Select(row => (Mutation)new PutDocument(PartitionQueryRf3Protocol.Collection,
                row.Id, Serialize(row), ExpectedRevision: 0)).ToImmutableArray();
            var command = new CommandRequest(Guid.NewGuid(), group.Key, puts);
            await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command, cancellationToken)
                .ConfigureAwait(false)).ConfigureAwait(false);
        }
    }

    private static async Task BindFirstPartitionAsync(KeyLoadClient administrator, ClusterFixture fixture,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadAtomicPartitionPlacementAsync(
            new(1, partition), cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var request = new BindAtomicPartitionPlacementRequest(1, before!.DirectoryRevision, partition,
            fixture.PhysicalShardId);
        var result = await administrator.BindAtomicPartitionPlacementAsync(Guid.NewGuid(), request, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(result.IsSuccess && result.Value).IsTrue();
    }

    private static async Task VerifyBoundAndFallbackAsync(KeyLoadClient administrator, ClusterFixture fixture,
        PartitionRef boundPartition, PartitionRef fallbackPartition, CancellationToken cancellationToken)
    {
        var bound = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadAtomicPartitionPlacementAsync(
            new(1, boundPartition), cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var fallback = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadAtomicPartitionPlacementAsync(
            new(1, fallbackPartition), cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(bound!.IsFallback).IsFalse();
        await Assert.That(bound.Revision).IsEqualTo(1L);
        await Assert.That(fallback!.IsFallback).IsTrue();
        await Assert.That(fallback.Revision).IsEqualTo(0L);
        await AtomicPartitionPlacementRf3Assertions.ValidOwnerAsync(bound, fixture.PhysicalShardId)
            .ConfigureAwait(false);
        await AtomicPartitionPlacementRf3Assertions.ValidOwnerAsync(fallback, fixture.PhysicalShardId)
            .ConfigureAwait(false);
        await AtomicPartitionPlacementRf3Assertions.SameOwnerAsync(bound, fallback).ConfigureAwait(false);
    }

    private static string Serialize(PartitionQueryRf3InputRow row)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PartitionQueryRf3Protocol.RankJsonProperty] = row.Rank,
            [PartitionQueryRf3Protocol.ValueAlias] = row.Value
        }, JsonDefaults.Options);
}
