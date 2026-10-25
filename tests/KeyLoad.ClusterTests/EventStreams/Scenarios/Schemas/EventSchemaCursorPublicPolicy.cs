using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.EventStreams;

internal static class EventSchemaCursorPublicPolicy
{
    internal static async Task ChangeAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        EventSchemaCursorPublicState state, long schema, CancellationToken token)
    {
        var desired = schema == 2L
            ? state.Initial with
            {
                SchemaVersion = 2L,
                HeaderPolicies = [new(EventSchemaCursorPublicExpected.HeaderPath,
                EventSchemaCursorPublicExpected.Classification, EventSchemaCursorPublicExpected.RawReadGrant)]
            }
            : state.Initial with
            {
                SchemaVersion = 3L
            };
        var request = new ConfigureResourceRequest(state.Scenario.Partition.TenantId, state.Scenario.Partition.DatabaseId, desired)
        {
            ExpectedSchemaVersion = schema == 2L ? 1L : 2L
        };
        await ApplyAsync(admin, reader, state, request, desired, token);
    }

    internal static Task VerifyCurrentAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        EventSchemaCursorPublicState state, CancellationToken token)
        => ApplyAsync(admin, reader, state, new(state.Scenario.Partition.TenantId,
            state.Scenario.Partition.DatabaseId, state.Definition), state.Definition, token);

    private static async Task ApplyAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        EventSchemaCursorPublicState state, ConfigureResourceRequest request, ResourceDefinition expected, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var actual = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(id, request, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
        state.Definition = expected;
        state.Configurations.Add((id, request, actual));
        await EventSchemaCursorPublicCalls.HealthyAsync(admin, reader, state, token);
        await ReplayOneAsync(admin, reader, state, id, request, actual, token);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        EventSchemaCursorPublicState state, CancellationToken token)
    {
        foreach (var original in state.Configurations)
        {
            await ReplayOneAsync(admin, reader, state, original.Id, original.Request, original.Result, token);
        }
    }

    private static async Task ReplayOneAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        EventSchemaCursorPublicState state, Guid id, ConfigureResourceRequest request, ResourceDefinition original, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(original,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(id, request, token)));
        await EventSchemaCursorPublicCalls.HealthyAsync(admin, reader, state, token);
        var arguments = McpOfficialClient.Arguments(request);
        arguments.Add(McpCallerProtocol.CommandId, id);
        var reply = await admin.Mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.ResourcesConfigure,
            arguments, cancellationToken: token);
        await SqlClusterProtocol.EqualAsync(original, (await McpCallerAssertions.SuccessAsync<ResourceDefinition>(reply)).Value);
        await EventSchemaCursorPublicCalls.HealthyAsync(admin, reader, state, token);
    }
}
