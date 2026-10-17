using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesResourceTagPolicyRf3Policy
{
    private const string StaleDetail = "The expected resource schema version is stale.";
    private const string McpStaleDetail = "The database operation could not be completed.";
    private const string DetailProperty = "detail";

    internal static ConfigureResourceRequest Request(TimeSeriesResourceTagPolicyRf3State state,
        ResourceDefinition definition, long? expected = null) => new(state.Scenario.Partition.TenantId,
            state.Scenario.Partition.DatabaseId, definition)
        {
            ExpectedSchemaVersion = expected
        };

    internal static async Task ChangeAsync(RequestCqrsRf3Callers administrator,
        TimeSeriesResourceTagPolicyRf3State state, string grant, CancellationToken token)
    {
        var current = state.Current;
        var next = current with
        {
            SchemaVersion = checked(current.SchemaVersion + 1),
            FieldPolicies = [current.FieldPolicies[0] with { RawReadGrant = grant, RawUseGrant = grant }]
        };
        var request = Request(state, next, current.SchemaVersion);
        var id = Guid.NewGuid();
        var actual = current.SchemaVersion == 1L
            ? await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigureResourceAsync(id, request, token))
            : await McpAsync(administrator, id, request, token);
        await SqlRf3Protocol.EqualAsync(next, actual);
        state.Configurations.Add((id, request, actual));
        state.Current = next;
        if (next.SchemaVersion == 2L)
        {
            state.Stale = Request(state, next, 1L);
        }
        await TimeSeriesResourceTagPolicyRf3Assertions.CurrentAsync(administrator, state, token);
    }

    internal static async Task WitnessAsync(RequestCqrsRf3Callers administrator,
        TimeSeriesResourceTagPolicyRf3State state, CancellationToken token)
    {
        await TimeSeriesResourceTagPolicyRf3Assertions.CurrentAsync(administrator, state, token);
        var request = Request(state, state.Current);
        // Fresh identical configuration commands validate current complete metadata.
        // They are writes, never cached historical-result reads.
        await SqlRf3Protocol.EqualAsync(state.Current, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(), request, token)));
        await TimeSeriesResourceTagPolicyRf3Assertions.CurrentAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(state.Current, await McpAsync(administrator, Guid.NewGuid(), request, token));
        await TimeSeriesResourceTagPolicyRf3Assertions.CurrentAsync(administrator, state, token);
        state.Outbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        TimeSeriesResourceTagPolicyRf3State state, CancellationToken token)
    {
        foreach (var original in state.Configurations)
        {
            await SqlRf3Protocol.EqualAsync(original.Result, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.ConfigureResourceAsync(original.Id, original.Request, token)));
            await AfterAsync(reader, administrator, state, token);
            await SqlRf3Protocol.EqualAsync(original.Result, await McpAsync(administrator, original.Id, original.Request, token));
            await AfterAsync(reader, administrator, state, token);
        }
        await SqlRf3Protocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(state.Append, token)));
        await AfterAsync(reader, administrator, state, token);
        await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Append, token))).Value);
        await AfterAsync(reader, administrator, state, token);
        await TimeSeriesResourceChunkPolicyRf3Enrollment.ReplayAsync(reader, administrator, state, token);
    }

    internal static async Task StaleAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        TimeSeriesResourceTagPolicyRf3State state, CancellationToken token)
    {
        var request = state.Stale ?? throw new InvalidOperationException("The original stale CAS has not been prepared.");
        var failed = await administrator.Sdk.ConfigureResourceAsync(state.StaleId, request, token);
        await Assert.That(failed.IsFailed).IsTrue();
        await Assert.That(failed.Value).IsNull();
        await Assert.That(failed.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.RevisionConflict));
        await Assert.That(failed.Problem?.Detail).IsEqualTo(StaleDetail);
        await AfterAsync(reader, administrator, state, token);
        var arguments = McpOfficialClient.Arguments(request);
        arguments.Add(McpCallerProtocol.CommandId, state.StaleId);
        var reply = await administrator.Mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.ResourcesConfigure,
            arguments, cancellationToken: token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.RevisionConflict, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(DetailProperty).GetString()).IsEqualTo(McpStaleDetail);
        await AfterAsync(reader, administrator, state, token);
    }

    private static async Task AfterAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        TimeSeriesResourceTagPolicyRf3State state, CancellationToken token)
    {
        await TimeSeriesResourceTagPolicyRf3Assertions.ReadAsync(reader, administrator, state, token);
        await WitnessAsync(administrator, state, token);
    }

    private static async Task<ResourceDefinition> McpAsync(RequestCqrsRf3Callers administrator, Guid id,
        ConfigureResourceRequest request, CancellationToken token)
    {
        var arguments = McpOfficialClient.Arguments(request);
        arguments.Add(McpCallerProtocol.CommandId, id);
        return (await McpCallerAssertions.SuccessAsync<ResourceDefinition>(
            await administrator.Mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.ResourcesConfigure,
                arguments, cancellationToken: token))).Value;
    }
}
