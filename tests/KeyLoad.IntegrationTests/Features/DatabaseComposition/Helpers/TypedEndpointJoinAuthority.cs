using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class TypedEndpointJoinAuthority
{
    private const long VersionStep = 1;
    private const int JoinedEffects = 1;

    internal static Task<TypedEndpointJoinState> WarmAsync(ClusterFixture fixture,
        TypedEndpointJoinSeed seed, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, seed.Identity.Secret, async callers =>
    {
        var emptyDenied = new CommandRequest(Guid.NewGuid(), seed.Partition,
            [TypedEndpointJoinProtocol.Mutation(TypedEndpointJoinProtocol.EmptyPrefix) with { Queue = TypedEndpointJoinProtocol.EmptyQueue }]);
        var emptyProblem = await DeniedAsync(callers, emptyDenied, token);
        var denied = new CommandRequest(Guid.NewGuid(), seed.Partition, [TypedEndpointJoinProtocol.Mutation()]);
        var originalProblem = await DeniedAsync(callers, denied, token);
        await SqlRf3Protocol.EqualAsync(seed.Models, await TypedEndpointJoinAssertions.CaptureAsync(admin, seed.Partition, token));
        var repaired = await PolicyAsync(admin, seed.Partition, seed.ToDefinition, TypedEndpointJoinProtocol.UseGrant, token);
        var repairedSeed = seed with { ToDefinition = repaired };
        await SqlRf3Protocol.EqualAsync(emptyProblem, await DeniedAsync(callers, emptyDenied, token));
        var emptyHealthy = emptyDenied with { CommandId = Guid.NewGuid() };
        var emptyReceipt = await SqlRf3Protocol.SdkAsync<CommitReceipt>(callers.Sdk, TypedEndpointJoinProtocol.Sql(emptyHealthy), token);
        await Assert.That(emptyReceipt.CommandId).IsEqualTo(emptyHealthy.CommandId);
        await Assert.That(emptyReceipt.Mutations.Length).IsEqualTo(TypedEndpointJoinProtocol.NoEffects);
        await TypedEndpointJoinAssertions.ReplayAsync(callers, emptyHealthy, emptyReceipt, token);
        await SqlRf3Protocol.EqualAsync(seed.Models, await TypedEndpointJoinAssertions.CaptureAsync(admin, seed.Partition, token));
        await SqlRf3Protocol.EqualAsync(originalProblem, await DeniedAsync(callers, denied, token));
        var healthy = new CommandRequest(Guid.NewGuid(), seed.Partition, [TypedEndpointJoinProtocol.Mutation()]);
        var receipt = await SqlRf3Protocol.SdkAsync<CommitReceipt>(callers.Sdk, TypedEndpointJoinProtocol.Sql(healthy), token);
        await Assert.That(receipt.CommandId).IsEqualTo(healthy.CommandId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(JoinedEffects);
        await TypedEndpointJoinAssertions.ReplayAsync(callers, healthy, receipt, token);
        var models = await TypedEndpointJoinAssertions.CaptureAsync(admin, seed.Partition, token);
        await TypedEndpointJoinAssertions.LiteralAsync(models, seed.Partition, continued: false, joined: true);
        var state = new TypedEndpointJoinState(repairedSeed, denied, originalProblem, healthy, receipt, emptyHealthy, emptyReceipt, models);
        await TypedEndpointJoinRefusals.RunAsync(admin, callers, state, token);
        return state;
    }, token), token);

    internal static async Task<ResourceDefinition> PolicyAsync(RequestCqrsRf3Callers admin, PartitionRef partition,
        ResourceDefinition current, string useGrant, CancellationToken token)
    {
        var replacement = current with
        {
            SchemaVersion = current.SchemaVersion + VersionStep,
            FieldPolicies = current.FieldPolicies.Select(policy => policy with { RawUseGrant = useGrant }).ToImmutableArray()
        };
        var actual = await TypedEndpointJoinSetup.ConfigureAsync(admin, partition, replacement, token, current.SchemaVersion);
        await SqlRf3Protocol.EqualAsync(replacement, actual);
        return actual;
    }

    internal static async Task<JsonElement> DeniedAsync(RequestCqrsRf3Callers callers,
        CommandRequest command, CancellationToken token)
    {
        var sql = TypedEndpointJoinProtocol.Sql(command);
        var result = await callers.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied)).Because(result.Problem.Detail ?? string.Empty);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token),
            ErrorCode.PermissionDenied, dispatched: true);
        return JsonSerializer.SerializeToElement(result.Problem, JsonDefaults.Options);
    }
}
