using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3BackpressureSeed
{
    internal static async Task<PostgresRf3BackpressureState> CreateAsync(ClusterFixture fixture, KeyLoadClient root, CancellationToken token)
    {
        var partition = PostgresRf3State.Partition;
        var resource = new ResourceDefinition(PostgresRf3BackpressureProtocol.Collection,
            ResourceKind.Collection, partition.TransactionDomainId);
        await SqlRf3Protocol.EqualAsync(resource, await SqlRf3Protocol.SdkAsync<ResourceDefinition>(root,
            SqlRf3Protocol.Call(partition, McpCallerTools.ResourcesConfigure,
                new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource), Guid.NewGuid()), token));
        var identity = await PostgresRf3Seed.IdentityAsync(root, PostgresRf3BackpressureProtocol.Identity,
            Capability.Query | Capability.DocumentsRead | Capability.DocumentsWrite, token, PostgresRf3BackpressureProtocol.Collection);
        var mutations = Enumerable.Range(PostgresRf3Protocol.OneColumn, PostgresRf3BackpressureProtocol.Rows)
            .Select(index => (Mutation)new PutDocument(PostgresRf3BackpressureProtocol.Collection,
                Id(index), Json(index))).ToImmutableArray();
        var command = new CommandRequest(Guid.NewGuid(), partition, mutations);
        return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            identity.Secret, async caller =>
        {
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
            await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token)));
            return new PostgresRf3BackpressureState(identity, command, receipt);
        }, token);
    }

    internal static string Id(int index)
        => PostgresRf3BackpressureProtocol.IdPrefix + index.ToString(CultureInfo.InvariantCulture);

    internal static string Json(int index)
        => JsonSerializer.Serialize(new
        {
            id = Id(index),
            payload = new string(PostgresRf3BackpressureProtocol.PayloadCharacter, PostgresRf3BackpressureProtocol.PayloadCharacters)
        },
            JsonDefaults.Options);
}
