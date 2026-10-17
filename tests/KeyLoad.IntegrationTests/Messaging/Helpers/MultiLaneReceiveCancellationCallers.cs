using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class MultiLaneReceiveCancellationCallers
{
    internal static async Task<RequestCqrsFaultMcpObservation> CallAsync(RequestCqrsRf3Callers caller,
        MultiLaneReceiveRequest request, CancellationToken token)
    {
        try
        {
            var result = await caller.Mcp.CallAsync(MultiLaneReceiveProtocol.ToolName, request, token)
                .ConfigureAwait(false);
            return new(result, null);
        }
        catch (OperationCanceledException error) { return new(null, error); }
        catch (HttpRequestException error) { return new(null, error); }
        catch (IOException error) { return new(null, error); }
    }
}
