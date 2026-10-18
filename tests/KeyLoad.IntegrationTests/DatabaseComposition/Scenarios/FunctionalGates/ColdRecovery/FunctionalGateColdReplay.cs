using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdReplay
{
    internal static async Task RunAsync(FunctionalGateContext context, FunctionalGateColdState state, CancellationToken token)
    {
        await Assert.That(state.Models.Length).IsEqualTo(3);
        foreach (var model in state.Models)
        {
            await SqlClusterProtocol.EqualAsync(model.TableDefinition,
                await FunctionalGateRelationalCalls.SqlSdkAsync<ResourceDefinition>(context, context.Administrator, model.TableCreate, token));
            await SqlClusterProtocol.EqualAsync(model.TableDefinition,
                await FunctionalGateRelationalCalls.SqlMcpAsync<ResourceDefinition>(context, context.Administrator, model.TableCreate, token));
            await SqlClusterProtocol.EqualAsync(model.Receipt, await context.McpAsync<CommitReceipt>(context.Writer,
                McpCallerTools.DocumentsCommit, model.Command, token));
            await SqlClusterProtocol.EqualAsync(model.Receipt, await context.SdkAsync(context.Writer.Sdk.CommitAsync(model.Command, token)));
            await SqlClusterProtocol.EqualAsync(model.TableReceipt,
                await FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(context, context.Writer, model.TableInsert, token));
            await SqlClusterProtocol.EqualAsync(model.TableReceipt,
                await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(context, context.Writer, model.TableInsert, token));
            await FunctionalGateColdBlobUpload.ReplayAsync(context, model.Blob, token);
            await FunctionalGateColdReadAssertions.VerifyAsync(context, model, token);
        }
    }
}
