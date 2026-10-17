using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueAckResponseLossRf3Authority
{
    private const long DemotedEpoch = 3;
    private const long RestoredEpoch = 4;

    internal static async Task VerifyAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers caller,
        MultiLaneReceiveRequest request, DeliveryCommand ack, MessageInspection committed, MessageInspection ready,
        string principalId, CancellationToken token)
    {
        await PolicyAsync(administrator, request, principalId, DemotedEpoch, Capability.QueueConsume, token);
        await QueueAckResponseLossRf3Assertions.ImagesAsync(administrator, request, committed, ready, token);
        var deniedAttempt = ack with
        {
            CommandId = Guid.NewGuid()
        };
        await RejectPairAsync(administrator, caller, request, ack, ErrorCode.PermissionDenied, committed, ready, token);
        await RejectPairAsync(administrator, caller, request, deniedAttempt, ErrorCode.PermissionDenied, committed, ready, token);
        await PolicyAsync(administrator, request, principalId, RestoredEpoch, Capability.QueueConsume | Capability.QueueAck, token);
        await QueueAckResponseLossRf3Assertions.ImagesAsync(administrator, request, committed, ready, token);
        await RejectPairAsync(administrator, caller, request, ack, ErrorCode.PermissionDenied, committed, ready, token);
        await RejectPairAsync(administrator, caller, request, deniedAttempt, ErrorCode.PermissionDenied, committed, ready, token);
        var stale = ack with
        {
            CommandId = Guid.NewGuid()
        };
        await RejectPairAsync(administrator, caller, request, stale, ErrorCode.StaleLease, committed, ready, token);
    }

    private static async Task PolicyAsync(RequestCqrsRf3Callers administrator, MultiLaneReceiveRequest request,
        string principalId, long epoch, Capability capabilities, CancellationToken token)
    {
        var partition = request.Requests[0].Lane.Partition;
        var principal = new PrincipalRecord(principalId, partition.TenantId,
            [.. request.Requests.Select(leaf => new ScopeGrant(partition.DatabaseId, leaf.Lane.Queue, capabilities))], [])
        {
            ClusterAdministrator = false,
            PolicyEpoch = epoch
        };
        var stored = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), principal, token).ConfigureAwait(false)).ConfigureAwait(false);
        await QueueAckResponseLossRf3Assertions.EqualAsync(stored, principal);
    }

    internal static async Task RejectPairAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers caller,
        MultiLaneReceiveRequest request, DeliveryCommand command, ErrorCode error,
        MessageInspection first, MessageInspection second, CancellationToken token)
    {
        await QueueLeaseRf3Assertions.DeniedAsync(await caller.Sdk.CompleteAsync(command, token).ConfigureAwait(false), error);
        await QueueAckResponseLossRf3Assertions.ImagesAsync(administrator, request, first, second, token);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.MessagesComplete,
            command, token).ConfigureAwait(false), error, dispatched: true).ConfigureAwait(false);
        await QueueAckResponseLossRf3Assertions.ImagesAsync(administrator, request, first, second, token);
    }

}
