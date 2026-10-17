using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdRetainedCalls
{
    internal static async Task RunAsync(FunctionalGateFixture fixture, FunctionalGateContext original,
        McpPersistedIdentity identity, Func<FunctionalGateContext, Task> action, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, fixture.NodeNames[^1], identity.Secret, async caller =>
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
        DropLifecycleRf3Route route, CancellationToken token)
        => route switch
        {
            DropLifecycleRf3Route.Sdk => context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token)),
            DropLifecycleRf3Route.Mcp => context.McpAsync<CommitReceipt>(context.Writer,
                McpCallerTools.DocumentsCommit, command, token),
            DropLifecycleRf3Route.SqlSdk => FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(context,
                context.Writer, FunctionalGateRelationalCalls.Commit(command), token),
            DropLifecycleRf3Route.SqlMcp => FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(context,
                context.Writer, FunctionalGateRelationalCalls.Commit(command), token),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
}
