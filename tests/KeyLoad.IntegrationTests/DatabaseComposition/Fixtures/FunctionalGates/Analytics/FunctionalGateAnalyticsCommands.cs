using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateAnalyticsCommands
{
    internal static async Task<CommitReceipt> CommitAsync(FunctionalGateContext context, CommandRequest command,
        string kind, string store, string lastId, long generation, CancellationToken token)
    {
        var receipt = await context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token));
        await FunctionalGateCompositionCalls.ReceiptAsync(context, receipt, command.CommandId,
            [new(kind, store, lastId, generation)]);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.McpAsync<CommitReceipt>(context.Writer,
            McpCallerTools.DocumentsCommit, FunctionalGateAnalyticsIdentity.Wire(command), token), receipt);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateCompositionCalls.CommandAsync(
            context, FunctionalGateAnalyticsIdentity.Rebuild(command), official: true, token), receipt);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateCompositionCalls.CommandAsync(
            context, FunctionalGateAnalyticsIdentity.Rebuild(command), official: false, token), receipt);
        return receipt;
    }
}
