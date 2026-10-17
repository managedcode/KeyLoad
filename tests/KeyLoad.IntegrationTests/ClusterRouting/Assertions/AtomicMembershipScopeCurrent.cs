using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class AtomicMembershipScopeCurrent
{
    internal static async Task RunAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers caller,
        RequestCqrsPhaseFaultIdentity identity, AtomicPartitionPlacementResolution placement,
        long revision, string json, OutboxStatus outbox, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(placement,
            await TenantPackingActivationRf3Admission.ReadAsync(admin, identity.Partition, token));
        await RequestCqrsFaultReplayContinuation.DocumentAsync(caller,
            new(identity.Partition, RequestCqrsRf3Protocol.AdminCollection, AtomicMembershipScopeCommands.Left),
            revision, json, token);
        await RequestCqrsFaultReplayContinuation.DocumentAsync(caller,
            new(identity.Partition, RequestCqrsRf3Protocol.AdminCollection, AtomicMembershipScopeCommands.Right),
            revision, json, token);
        await RequestCqrsPhaseFaultAssertions.VerifyDocumentAsync(caller, identity,
            RequestCqrsRf3Protocol.DocumentJson, 3, token);
        await SqlRf3Protocol.EqualAsync(outbox,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(identity.Partition, token)));
        await SqlRf3Protocol.EqualAsync(outbox,
            (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await admin.Mcp.CallAsync(
                McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(identity.Partition), token))).Value);
    }
}
