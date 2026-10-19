using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class TypedEndpointJoinColdFlow
{
    private const int ContinuationEffects = 2;

    internal static async Task RunAsync(ClusterFixture fixture, TypedEndpointJoinState original, CancellationToken token)
    {
        original = await TypedEndpointJoinReverseFlow.WarmAsync(fixture, original, token);
        await ModelSqlClusterCold.RunAsync(fixture, token);
        var continued = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, original.Seed.Identity.Secret,
                async callers => await ContinueAsync(fixture, admin, callers, original, token), token), token);
        await ModelSqlClusterCold.RunAsync(fixture, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, fixture.AdminKey, async admin =>
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, continued.Seed.Identity.Secret, async callers =>
        {
            await AssertOriginalAsync(fixture, admin, callers, continued, token);
            await TypedEndpointJoinAssertions.LiteralAsync(continued.Models, continued.Seed.Partition, continued: true, joined: true);
            return true;
        }, token), token);
    }

    private static async Task<TypedEndpointJoinState> ContinueAsync(ClusterFixture fixture, RequestCqrsClusterCallers admin,
        RequestCqrsClusterCallers callers, TypedEndpointJoinState state, CancellationToken token)
    {
        await AssertOriginalAsync(fixture, admin, callers, state, token);
        var revoked = await TypedEndpointJoinAuthority.PolicyAsync(admin, state.Seed.Partition, state.Seed.ToDefinition,
            TypedEndpointJoinProtocol.BlockedUseGrant, token);
        await TypedEndpointJoinAuthority.DeniedAsync(callers, state.Healthy, token);
        await TypedEndpointJoinAuthority.DeniedAsync(callers, state.EmptyHealthy, token);
        await SqlClusterProtocol.EqualAsync(state.Models, await TypedEndpointJoinAssertions.CaptureAsync(admin, state.Seed.Partition, token));
        var repaired = await TypedEndpointJoinAuthority.PolicyAsync(admin, state.Seed.Partition, revoked,
            TypedEndpointJoinProtocol.UseGrant, token);
        state = state with
        {
            Seed = state.Seed with
            {
                ToDefinition = repaired
            }
        };
        await TypedEndpointJoinAssertions.ReplayAsync(callers, state.Healthy, state.Receipt, token);
        await TypedEndpointJoinAssertions.ReplayAsync(callers, state.EmptyHealthy, state.EmptyReceipt, token);
        state = await MalformedSourceAsync(admin, callers, state, token);
        var repair = new CommandRequest(Guid.NewGuid(), state.Seed.Partition, [TypedEndpointJoinAssertions.RepairMissing(state)]);
        var repairReceipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(repair, token));
        await Assert.That(repairReceipt.CommandId).IsEqualTo(repair.CommandId);
        await Assert.That(repairReceipt.Mutations).HasSingleItem();
        var changedSources = await TypedEndpointJoinAssertions.CaptureAsync(admin, state.Seed.Partition, token);
        await TypedEndpointJoinAssertions.ReplayAsync(callers, state.Healthy, state.Receipt, token);
        await SqlClusterProtocol.EqualAsync(changedSources,
            await TypedEndpointJoinAssertions.CaptureAsync(admin, state.Seed.Partition, token));
        var continuation = new CommandRequest(Guid.NewGuid(), state.Seed.Partition,
            [TypedEndpointJoinProtocol.Mutation(TypedEndpointJoinAssertions.ContinuedPrefix)]);
        var receipt = await SqlClusterProtocol.McpAsync<CommitReceipt>(callers.Mcp, TypedEndpointJoinProtocol.Sql(continuation), token);
        await Assert.That(receipt.CommandId).IsEqualTo(continuation.CommandId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(ContinuationEffects);
        await TypedEndpointJoinAssertions.ReplayAsync(callers, continuation, receipt, token);
        var models = await TypedEndpointJoinAssertions.CaptureAsync(admin, state.Seed.Partition, token);
        await TypedEndpointJoinAssertions.LiteralAsync(models, state.Seed.Partition, continued: true, joined: true);
        return state with
        {
            Models = models,
            Continuation = continuation,
            ContinuationReceipt = receipt
        };
    }

    private static async Task AssertOriginalAsync(ClusterFixture fixture, RequestCqrsClusterCallers admin, RequestCqrsClusterCallers callers,
        TypedEndpointJoinState state, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(state.Models, await TypedEndpointJoinAssertions.CaptureAsync(admin, state.Seed.Partition, token));
        await SqlClusterProtocol.EqualAsync(state.OriginalProblem,
            await TypedEndpointJoinAuthority.DeniedAsync(callers, state.OriginalDenied, token));
        await TypedEndpointJoinAssertions.ReplayAsync(callers, state.Healthy, state.Receipt, token);
        if (state.Reverse is not null)
        {
            await TypedEndpointJoinReverseAssertions.OriginalAsync(fixture, admin, state, token);
        }
        if (state.Malformed is { } malformed && state.MalformedProblem is { } problem)
        {
            await SqlClusterProtocol.EqualAsync(problem, await RefusedMalformedAsync(callers, malformed, token));
        }
        if (state.Continuation is { } continuation && state.ContinuationReceipt is { } receipt)
        {
            await TypedEndpointJoinAssertions.ReplayAsync(callers, continuation, receipt, token);
        }
        await SqlClusterProtocol.EqualAsync(state.Models, await TypedEndpointJoinAssertions.CaptureAsync(admin, state.Seed.Partition, token));
    }

    private static async Task<TypedEndpointJoinState> MalformedSourceAsync(RequestCqrsClusterCallers admin,
        RequestCqrsClusterCallers callers, TypedEndpointJoinState state, CancellationToken token)
    {
        var to = TypedEndpointJoinProtocol.To(state.Seed.Partition);
        var payload = DatabaseCompositionClusterPayload.OrdinalGolden(new QueueGraphLink(to, to,
            TypedEndpointJoinProtocol.Label, TypedEndpointJoinProtocol.Attributes));
        var seed = new CommandRequest(Guid.NewGuid(), state.Seed.Partition,
            [new EnqueueMessage(TypedEndpointJoinProtocol.EmptyQueue, TypedEndpointJoinProtocol.MalformedMessage, payload)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(seed, token));
        await Assert.That(receipt.CommandId).IsEqualTo(seed.CommandId);
        await Assert.That(receipt.Mutations).HasSingleItem();
        var models = await TypedEndpointJoinAssertions.CaptureAsync(admin, state.Seed.Partition, token);
        await SqlClusterProtocol.EqualAsync(state.Models, models with
        {
            MalformedSource = null
        });
        await TypedEndpointJoinAssertions.ReplayAsync(callers, state.EmptyHealthy, state.EmptyReceipt, token);
        await SqlClusterProtocol.EqualAsync(models, await TypedEndpointJoinAssertions.CaptureAsync(admin, state.Seed.Partition, token));
        var command = state.EmptyHealthy with
        {
            CommandId = Guid.NewGuid()
        };
        var problem = await RefusedMalformedAsync(callers, command, token);
        await SqlClusterProtocol.EqualAsync(problem, await RefusedMalformedAsync(callers, command, token));
        await SqlClusterProtocol.EqualAsync(models, await TypedEndpointJoinAssertions.CaptureAsync(admin, state.Seed.Partition, token));
        return state with
        {
            Models = models,
            Malformed = command,
            MalformedProblem = problem
        };
    }

    private static async Task<JsonElement> RefusedMalformedAsync(RequestCqrsClusterCallers callers,
        CommandRequest command, CancellationToken token)
    {
        var request = TypedEndpointJoinProtocol.Sql(command);
        var result = await callers.Sdk.ExecuteSqlAsync(request, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token),
            ErrorCode.Validation, dispatched: true);
        return JsonSerializer.SerializeToElement(result.Problem, JsonDefaults.Options);
    }
}
