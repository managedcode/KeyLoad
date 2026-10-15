using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class UnacknowledgedArchiveHistory
{
    internal static Task RunAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CancellationToken token)
    {
        var request = new ReadEventSourceRequest(state.Source);
        return UnacknowledgedArchiveFailures.RequireAsync(callers, state, McpCallerTools.EventsRead,
            request, () => callers.Sdk.ReadEventSourceAsync(request, token), ErrorCode.HistoryUnavailable,
            UnacknowledgedArchiveProtocol.HistoryUnavailable, UnacknowledgedArchiveProtocol.SafeFailure, token);
    }
}
