using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Creates independently scoped real RF3 collections through the actual public HTTP SDK.</summary>
/// <param name="Partition">The newly configured actual partition identity.</param>
internal sealed record McpDocumentScenario(PartitionRef Partition)
{
    /// <summary>Gets the actual document reference used by both public protocol adapters.</summary>
    internal EntityRef Reference => new(Partition, McpDocumentProtocol.Collection, McpDocumentProtocol.Entity);

    /// <summary>Creates a real resource in a unique tenant without changing cluster configuration.</summary>
    /// <param name="fixture">The actual Docker/Aspire RF3 application.</param>
    /// <param name="cancellationToken">The bounded external caller lifetime.</param>
    /// <returns>The configured real document scenario.</returns>
    internal static Task<McpDocumentScenario> CreateAsync(ClusterFixture fixture, CancellationToken cancellationToken)
        => ConfigureAsync(fixture, [], cancellationToken);

    /// <summary>Creates a real collection with persisted sensitive-field projection and write restrictions.</summary>
    /// <param name="fixture">The actual Docker/Aspire RF3 application.</param>
    /// <param name="cancellationToken">The bounded external caller lifetime.</param>
    /// <returns>The actual protected collection scenario.</returns>
    internal static Task<McpDocumentScenario> CreateProtectedAsync(ClusterFixture fixture, CancellationToken cancellationToken)
        => ConfigureAsync(fixture, [new(McpDocumentProtocol.ProtectedPath, McpDocumentProtocol.ProtectedClassification)], cancellationToken);

    private static async Task<McpDocumentScenario> ConfigureAsync(ClusterFixture fixture,
        ImmutableArray<SensitiveFieldPolicy> policies, CancellationToken cancellationToken)
    {
        var partition = new PartitionRef(McpDocumentProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            McpDocumentProtocol.Database, McpDocumentProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey);
        var definition = new ResourceDefinition(McpDocumentProtocol.Collection, ResourceKind.Collection, partition.TransactionDomainId)
        { FieldPolicies = policies };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, definition),
            cancellationToken));
        return new(partition);
    }

    /// <summary>Creates an actual canonical mutation preserving the caller's stable command identity.</summary>
    /// <param name="commandId">The caller-owned stable command GUID.</param>
    /// <param name="json">The optional changed canonical document JSON.</param>
    /// <returns>The actual public database command.</returns>
    internal CommandRequest Command(Guid commandId, string? json = null) => new(commandId, Partition,
        [new PutDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity, json ?? McpDocumentProtocol.InitialJson,
            McpDocumentProtocol.ExpectedAbsentRevision)]);

    /// <summary>Seeds the actual document through the genuine HTTP SDK.</summary>
    /// <param name="fixture">The initialized real RF3 cluster.</param>
    /// <param name="cancellationToken">The bounded caller lifetime.</param>
    /// <returns>The completed actual committed-write assertion.</returns>
    internal async Task SeedAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        await McpCallerAssertions.SdkSuccessAsync(await new KeyLoadClient(http, fixture.AdminKey).CommitAsync(
            Command(Guid.NewGuid()), cancellationToken));
    }
}
