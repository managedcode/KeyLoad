using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryModelsPolicyReplay
{
    internal static async Task VerifyAsync(FunctionalGateContext context,
        ResourcePartitionInventoryModelsState state, CancellationToken token)
    {
        foreach (var creation in state.Creations)
        {
            await ResourcePartitionInventoryPolicyRefusal.CreationAsync(context.Administrator, creation, token);
        }
        foreach (var original in state.Batches)
        {
            await ResourcePartitionInventoryPolicyRefusal.SdkAsync(
                await context.Writer.Sdk.CommitAsync(original.Command, token));
            await ResourcePartitionInventoryPolicyRefusal.McpAsync(await context.Writer.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, original.Command, token));
        }
        await SqlAsync(context.Administrator, state.Model.TableCreate, token);
        await SqlAsync(context.Writer, state.Model.TableInsert, token);
        await BlobAsync(context, state.Model.Blob, token);
    }

    private static async Task SqlAsync(RequestCqrsRf3Callers callers,
        SqlOperationRequest request, CancellationToken token)
    {
        await ResourcePartitionInventoryPolicyRefusal.SdkAsync(await callers.Sdk.ExecuteSqlAsync(request, token));
        await ResourcePartitionInventoryPolicyRefusal.McpAsync(
            await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token));
    }

    private static async Task BlobAsync(FunctionalGateContext context,
        FunctionalGateColdBlob ledger, CancellationToken token)
    {
        await ResourcePartitionInventoryPolicyRefusal.SdkAsync(
            await context.Writer.Sdk.BeginBlobUploadAsync(ledger.Begin, token));
        await ResourcePartitionInventoryPolicyRefusal.McpAsync(await context.Writer.Mcp.CallAsync(
            "keyload_blobs_begin_upload", ledger.Begin, token));
        foreach (var part in ledger.Parts)
        {
            await ResourcePartitionInventoryPolicyRefusal.SdkAsync(
                await context.Writer.Sdk.WriteBlobPartAsync(part.Request, token));
            await ResourcePartitionInventoryPolicyRefusal.McpAsync(await context.Writer.Mcp.CallAsync(
                "keyload_blobs_write_part", part.Request, token));
        }
        await ResourcePartitionInventoryPolicyRefusal.SdkAsync(
            await context.Writer.Sdk.CompleteBlobUploadAsync(ledger.Complete, token));
        await ResourcePartitionInventoryPolicyRefusal.McpAsync(await context.Writer.Mcp.CallAsync(
            "keyload_blobs_complete_upload", ledger.Complete, token));
    }
}
