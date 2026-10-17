using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryModelsReplay
{
    internal static async Task VerifyAsync(FunctionalGateContext context,
        ResourcePartitionInventoryModelsState state, CancellationToken token)
    {
        foreach (var creation in state.Creations)
        {
            await SqlRf3Protocol.EqualAsync(creation.Request.Definition,
                await context.SdkAsync(context.Administrator.Sdk.ConfigureResourceAsync(
                    creation.CommandId, creation.Request, token)));
            await SqlRf3Protocol.EqualAsync(creation.Request.Definition,
                await ResourcePartitionInventoryRf3Metadata.ConfigureAsync(context.Administrator, creation, token));
        }
        foreach (var original in state.Batches)
        {
            await SqlRf3Protocol.EqualAsync(original.Receipt,
                await context.SdkAsync(context.Writer.Sdk.CommitAsync(original.Command, token)));
            await SqlRf3Protocol.EqualAsync(original.Receipt,
                await context.McpAsync<CommitReceipt>(context.Writer, McpCallerTools.DocumentsCommit, original.Command, token));
        }
        await SqlRf3Protocol.EqualAsync(state.Model.TableDefinition,
            await FunctionalGateRelationalCalls.SqlSdkAsync<ResourceDefinition>(
                context, context.Administrator, state.Model.TableCreate, token));
        await SqlRf3Protocol.EqualAsync(state.Model.TableDefinition,
            await FunctionalGateRelationalCalls.SqlMcpAsync<ResourceDefinition>(
                context, context.Administrator, state.Model.TableCreate, token));
        await SqlRf3Protocol.EqualAsync(state.Model.TableReceipt,
            await FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(context, context.Writer, state.Model.TableInsert, token));
        await SqlRf3Protocol.EqualAsync(state.Model.TableReceipt,
            await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(context, context.Writer, state.Model.TableInsert, token));
        await FunctionalGateColdBlobUpload.ReplayAsync(context, state.Model.Blob, token);
        await FunctionalGateColdReadAssertions.VerifyAsync(context, state.Model, token);
    }
}
