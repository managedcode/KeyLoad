using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationOriginalSessionPolicy
{
    internal static async Task RunAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers writer, NativeActivationOriginalSessionState state, CancellationToken token)
    {
        var denied = state.ReaderPrincipal with
        {
            PolicyEpoch = 2L,
            Grants = []
        };
        var repaired = state.ReaderPrincipal with
        {
            PolicyEpoch = 3L
        };
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await ConfigureAsync(administrator, denied, token);
            for (var route = 0; route < 4; route++)
            {
                var originalRoute = route;
                await ServerFailureObserver.ObserveAsync(() => NativeActivationOriginalSessionConservation.ObserveAsync(
                    administrator, state, NativeActivationRf3Protocol.CommittedRevision,
                    RequestCqrsRf3Protocol.ChangedDocumentJson,
                    () => NativeActivationOriginalSessionReads.DeniedAsync(state.Readers, state, originalRoute, token), token), failures);
            }
        }, failures);
        // Repair is also owned cleanup after an uncertain Configure/denial failure; the original body deadline is unchanged.
        using (var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System))
        {
            await ServerFailureObserver.ObserveAsync(() => ConfigureAsync(administrator, repaired, cleanup.Token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => NativeActivationOriginalSessionFlow.ReadAsync(
            administrator, writer, state, NativeActivationRf3Protocol.CommittedRevision,
            RequestCqrsRf3Protocol.ChangedDocumentJson, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ConfigureAsync(RequestCqrsRf3Callers administrator,
        PrincipalRecord expected, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
}
