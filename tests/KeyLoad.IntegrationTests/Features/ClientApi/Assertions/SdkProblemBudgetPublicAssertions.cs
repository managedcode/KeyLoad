using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class SdkProblemBudgetPublicAssertions
{
    private const string Unavailable = "The server response is unavailable.";

    internal static async Task FailureAsync<T>(Result<T> actual, ErrorCode code, bool bounded)
    {
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Problem).IsNotNull();
        await Assert.That(actual.Problem!.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(actual.Problem.StatusCode).IsEqualTo(Errors.Status(code));
        if (bounded)
        {
            await SqlRf3Protocol.EqualAsync(Errors.Problem(code, Unavailable), actual.Problem);
        }
    }

    internal static async Task ReceiptAsync(CommitReceipt actual, CommandRequest command,
        AtomicPartitionPlacementResolution placement, MutationReceipt[] expected)
    {
        await Assert.That(actual.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(actual.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, actual.Mutations.ToArray());
    }

    internal static DocumentResult Allowed(SdkProblemBudgetPublicState state)
        => new(state.Allowed, state.HealthyReceipt is null ? 1L : 2L,
            state.HealthyReceipt is null ? SdkProblemBudgetPublicPreparation.AllowedJson
                : SdkProblemBudgetPublicPreparation.HealthyJson, false, []);

    internal static async Task CurrentAsync(RequestCqrsRf3Callers administrator,
        SdkProblemBudgetPublicState state, CancellationToken token)
    {
        var minimum = state.HealthyReceipt?.Token ?? state.SeedReceipt.Token;
        await DocumentAsync(administrator.Sdk, administrator.Mcp, state.Allowed, minimum, Allowed(state), token);
        var forbidden = new DocumentResult(state.Forbidden, 1L, SdkProblemBudgetPublicPreparation.ForbiddenJson, false, []);
        await DocumentAsync(administrator.Sdk, administrator.Mcp, state.Forbidden, minimum, forbidden, token);
        await SqlRf3Protocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Seed.Partition, token)));
        await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Seed.Partition), token))).Value);
    }

    internal static async Task DocumentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        EntityRef reference, CommitToken minimum, DocumentResult expected, CancellationToken token)
    {
        var actual = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(reference, minimum, token));
        await SqlRf3Protocol.EqualAsync(expected, actual!);
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.RequiredDocumentAsync(
            await mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, minimum), token))).Value);
    }

    internal static async Task HealthyAsync(SdkProblemBudgetPublicCallers caller,
        SdkProblemBudgetPublicState state, CancellationToken token)
    {
        var minimum = state.HealthyReceipt?.Token ?? state.SeedReceipt.Token;
        await DocumentAsync(caller.Minimum, caller.Mcp, state.Allowed, minimum, Allowed(state), token);
        var normal = await PublicReadAssertions.DocumentAsync(await caller.Normal.GetAsync(state.Allowed, minimum, token));
        await SqlRf3Protocol.EqualAsync(Allowed(state), normal!);
    }
}
