using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Messaging;

internal static class CompetingInboxPublicReplay
{
    internal static async Task RunAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers sdk, RequestCqrsClusterCallers official, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var (request, receipt) in state.Producers)
        {
            await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.EqualAsync(receipt,
                await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(request, token))), failures);
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.RequireAsync(state, root, token), failures);
            await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.EqualAsync(receipt,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token))).Value), failures);
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.RequireAsync(state, root, token), failures);
        }
        foreach (var (request, receipt) in state.Current)
        {
            await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.EqualAsync(receipt,
                await McpCallerAssertions.SdkSuccessAsync(await sdk.Sdk.CommitProcessingAsync(request, token))), failures);
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.RequireAsync(state, root, token), failures);
            await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.EqualAsync(receipt,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await official.Mcp.CallAsync(McpCallerTools.MessagesProcess, request, token))).Value), failures);
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.RequireAsync(state, root, token), failures);
        }
        foreach (var candidate in state.Candidates)
        {
            await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicPolicy.RefuseAsync(state, root, sdk, official,
                candidate, ErrorCode.PermissionDenied, CompetingInboxPublicProtocol.EpochChanged, token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicPolicy.RefuseAsync(state, root, sdk, official,
            state.StaleCurrent, ErrorCode.StaleLease, CompetingInboxPublicProtocol.Stale, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(state.Current.Count).IsEqualTo(CompetingInboxPublicProtocol.CurrentReceipts), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
