using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Messaging;

internal static class CompetingInboxPublicPolicy
{
    internal static async Task RepairAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers sdk, RequestCqrsClusterCallers official, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => SetAsync(state, root, CompetingInboxPublicProtocol.WithdrawnEpoch, false, token), failures);
        foreach (var candidate in state.Candidates)
        {
            await ServerFailureObserver.ObserveAsync(() => RefuseAsync(state, root, sdk, official, candidate,
                ErrorCode.PermissionDenied, CompetingInboxPublicProtocol.RequiredDenied, token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => SetAsync(state, root, CompetingInboxPublicProtocol.RepairedEpoch, true, token), failures);
        foreach (var candidate in state.Candidates)
        {
            await ServerFailureObserver.ObserveAsync(() => RefuseAsync(state, root, sdk, official, candidate,
                ErrorCode.PermissionDenied, CompetingInboxPublicProtocol.EpochChanged, token), failures);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var reconciled = state.Candidates[state.WinnerIndex] with
            {
                CommandId = Guid.NewGuid()
            };
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.Sdk.CommitProcessingAsync(reconciled, token));
            state.Current.Add((reconciled, receipt));
            await CompetingInboxPublicValues.EqualAsync(state.Winner, receipt);
            await CompetingInboxPublicModels.RequireAsync(state, root, token);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task SetAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers root,
        long epoch, bool allowed, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            FieldGrants = allowed ? [CompetingInboxPublicProtocol.RequiredRead] : []
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await CompetingInboxPublicValues.EqualAsync(expected, actual);
        state.Identity = state.Identity with
        {
            Principal = expected
        };
    }
    internal static async Task RefuseAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers sdk, RequestCqrsClusterCallers official, ProcessingRequest request,
        ErrorCode code, string detail, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.SdkFailureAsync(state,
            await sdk.Sdk.CommitProcessingAsync(request, token), code, detail), failures);
        await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.RequireAsync(state, root, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.McpFailureAsync(state,
            await official.Mcp.CallAsync(McpCallerTools.MessagesProcess, request, token), code), failures);
        await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.RequireAsync(state, root, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
