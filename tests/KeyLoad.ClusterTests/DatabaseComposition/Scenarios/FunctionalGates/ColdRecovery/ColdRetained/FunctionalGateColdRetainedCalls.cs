using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.RelationalStorage;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateColdRetainedCalls
{
    internal static async Task RunAsync(FunctionalGateFixture fixture, FunctionalGateContext original,
        McpPersistedIdentity identity, Func<FunctionalGateContext, Task> action, CancellationToken token)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, fixture.NodeNames[^1], identity.Secret, async caller =>
        {
            await action(original with
            {
                Writer = caller,
                Reader = caller,
                WriterPrincipalId = identity.Principal.Id,
                ReaderPrincipalId = identity.Principal.Id
            });
            return true;
        }, token);
    }

    internal static Task<CommitReceipt> CommitAsync(FunctionalGateContext context, CommandRequest command,
        DropLifecycleClusterRoute route, CancellationToken token)
        => route switch
        {
            DropLifecycleClusterRoute.Sdk => context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token)),
            DropLifecycleClusterRoute.Mcp => context.McpAsync<CommitReceipt>(context.Writer,
                McpCallerTools.DocumentsCommit, command, token),
            DropLifecycleClusterRoute.SqlSdk => FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(context,
                context.Writer, FunctionalGateRelationalCalls.Commit(command), token),
            DropLifecycleClusterRoute.SqlMcp => FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(context,
                context.Writer, FunctionalGateRelationalCalls.Commit(command), token),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
}
