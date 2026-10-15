using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementCleanupReadBudgetRf3Calls
{
    private const string PublicCapacity = "The operation exceeds the available resource capacity.";
    internal static async Task RefusalsAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var request = seed.FirstRequest with
        {
            Mode = PartitionMoveMode.Abort
        };
        var sdk = await seed.Source.MovePartitionAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity), sdk.Problem);
        await seed.VerifyAsync(token);
        var mcp = await seed.Official.CallAsync(PartitionMovePublicProtocol.ToolName, request, token);
        _ = await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.BudgetExceeded, dispatched: true);
        var structured = mcp.StructuredContent ?? throw new InvalidOperationException(McpCallerProtocol.MissingStructuredResult);
        await SqlRf3Protocol.EqualAsync(JsonSerializer.SerializeToElement(
            Errors.Problem(ErrorCode.BudgetExceeded, PublicCapacity), JsonDefaults.Options),
            structured.GetProperty(McpCallerProtocol.Error));
        await McpCallerAssertions.DoesNotDiscloseAsync(mcp, seed.Credential, RelationalSqlRf3Tokens.FirstRow);
        await seed.VerifyAsync(token);
    }
}
