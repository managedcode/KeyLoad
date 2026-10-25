using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Exercises the original writer and two read roles on distinct original model resources.</summary>
internal static class FunctionalGateColdConcurrentPrimary
{
    internal static async Task RunAsync(FunctionalGateContext context, FunctionalGateColdState state,
        int index, CancellationToken token)
    {
        var model = state.Models.Single(value => value.Index == index);
        if (index == 0)
        {
            await SqlClusterProtocol.EqualAsync(model.Receipt,
                await context.SdkAsync(context.Writer.Sdk.CommitAsync(model.Command, token)));
            await SqlClusterProtocol.EqualAsync(model.Receipt,
                await context.McpAsync<CommitReceipt>(context.Writer, McpCallerTools.DocumentsCommit, model.Command, token));
            await SqlClusterProtocol.EqualAsync(model.TableReceipt,
                await FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(context, context.Writer, model.TableInsert, token));
            await SqlClusterProtocol.EqualAsync(model.TableReceipt,
                await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(context, context.Writer, model.TableInsert, token));
        }
        await FunctionalGateColdReadAssertions.VerifyAsync(context, model, token);
        await FunctionalGateColdRelationalClosure.VerifyAsync(context, state, token);
    }
}
