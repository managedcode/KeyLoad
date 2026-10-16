using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Exercises the original writer and two read roles on distinct original model resources.</summary>
internal static class FunctionalGateColdConcurrentPrimary
{
    private const int WriterIndex = 0;

    internal static async Task RunAsync(FunctionalGateContext context, FunctionalGateColdState state,
        int index, CancellationToken token)
    {
        var model = state.Models.Single(value => value.Index == index);
        if (index == WriterIndex)
        {
            await SqlRf3Protocol.EqualAsync(model.Receipt,
                await context.SdkAsync(context.Writer.Sdk.CommitAsync(model.Command, token)));
            await SqlRf3Protocol.EqualAsync(model.Receipt,
                await context.McpAsync<CommitReceipt>(context.Writer, McpCallerTools.DocumentsCommit, model.Command, token));
            await SqlRf3Protocol.EqualAsync(model.TableReceipt,
                await FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(context, context.Writer, model.TableInsert, token));
            await SqlRf3Protocol.EqualAsync(model.TableReceipt,
                await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(context, context.Writer, model.TableInsert, token));
        }
        await FunctionalGateColdReadAssertions.VerifyAsync(context, model, token);
        await FunctionalGateColdRelationalClosure.VerifyAsync(context, state, token);
    }
}
