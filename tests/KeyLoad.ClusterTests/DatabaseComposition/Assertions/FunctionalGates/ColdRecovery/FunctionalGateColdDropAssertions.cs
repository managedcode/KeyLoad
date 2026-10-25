using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateColdDropAssertions
{
    internal static async Task TableAsync(FunctionalGateContext context, FunctionalGateColdDropTable table,
        CancellationToken token)
    {
        var expected = new DocumentReadResult(table.Fresh);
        await SqlClusterProtocol.EqualAsync(expected, await context.SdkAsync(
            context.Reader.Sdk.GetAsync(table.Fresh.Reference, token)));
        await SqlClusterProtocol.EqualAsync(expected, await context.McpAsync<DocumentReadResult>(context.Reader,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(table.Fresh.Reference), token));
        foreach (var id in new[] { table.OriginalReference.Id, FunctionalGateColdDropRows.AdministratorId })
        {
            var absent = table.Fresh.Reference with
            {
                Id = id
            };
            await SqlClusterProtocol.EqualAsync(new DocumentReadResult(null), await context.SdkAsync(
                context.Reader.Sdk.GetAsync(absent, token)));
            await SqlClusterProtocol.EqualAsync(new DocumentReadResult(null),
                await context.McpAsync<DocumentReadResult>(context.Reader, McpCallerTools.DocumentsGet,
                    new GetDocumentRequest(absent), token));
        }
    }

    internal static async Task AllAsync(FunctionalGateContext context, FunctionalGateColdDropState state,
        CancellationToken token)
    {
        foreach (var table in state.Tables)
        {
            await TableAsync(context, table, token);
        }
    }
}
