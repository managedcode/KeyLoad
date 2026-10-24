using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

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
            await FunctionalGateFiles.VerifyAsync(context.Reader, model.Blob, token);
            await FunctionalGateColdReadAssertions.VerifyAsync(context, model, token);
        }
    }
}
