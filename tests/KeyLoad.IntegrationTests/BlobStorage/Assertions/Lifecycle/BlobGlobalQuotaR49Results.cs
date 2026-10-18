using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

using ManagedCode.Communication;
internal static class BlobGlobalQuotaR49Results
{
    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid id, BlobGlobalQuotaR49State state)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations.IsEmpty).IsTrue();
    }
    internal static async Task ReplayAsync<T>(RequestCqrsClusterCallers callers, string tool, object request,
        T expected, Func<CancellationToken, Task<Result<T>>> sdk, BlobGlobalQuotaR49State state, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await sdk(token)));
        await BlobGlobalQuotaR49Current.RunAsync(callers, state, token);
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<T>(await callers.Mcp.CallAsync(tool, request, token))).Value);
        await BlobGlobalQuotaR49Current.RunAsync(callers, state, token);
    }
    internal static async Task FailureAsync<T>(RequestCqrsClusterCallers callers, string tool, object request,
        Func<CancellationToken, Task<Result<T>>> sdk, ErrorCode code, string detail,
        BlobGlobalQuotaR49State state, CancellationToken token)
    {
        var result = await sdk(token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
        await BlobGlobalQuotaR49Current.RunAsync(callers, state, token);
        var reply = await callers.Mcp.CallAsync(tool, request, token);
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        var safe = code == ErrorCode.ResourceExhausted ? "The operation exceeds the available resource capacity." : "The database operation could not be completed.";
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error).GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(safe);
        await BlobGlobalQuotaR49Current.RunAsync(callers, state, token);
    }
}
