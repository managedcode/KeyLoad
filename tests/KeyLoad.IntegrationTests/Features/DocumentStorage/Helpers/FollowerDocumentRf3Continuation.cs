using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal sealed class FollowerDocumentRf3Continuation(FollowerDocumentRf3State state)
{
    internal async Task VerifyHealthyAsync(KeyLoadClient sdk, RequestCqrsRf3Callers currentCaller,
        string currentReplica, CancellationToken token)
    {
        var status = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token).ConfigureAwait(false)).ConfigureAwait(false);
        await FollowerDocumentRf3Assertions.FollowerStatusAsync(status, currentReplica);
        var minimum = new CommitToken(state.Placement.Incarnation, state.Identity.Partition.AtomicPartitionId, status.Applied, state.Placement.PlacementEpoch);
        var healthy = state.Request with
        {
            ReplicaId = currentReplica,
            MaximumLagPositions = FollowerDocumentRf3Protocol.UnlimitedLag,
            MinimumToken = minimum
        };
        foreach (var transport in Enum.GetValues<FollowerDocumentCaller>())
        {
            var wrongReplica = await FollowerDocumentPublicObservation.InvokeAsync(transport, currentCaller.Sdk,
                currentCaller.Mcp, healthy with { ReplicaId = status.Leader! }, token).ConfigureAwait(false);
            await FollowerDocumentRf3Assertions.RejectedAsync(wrongReplica, ErrorCode.OwnershipLost, state.Identity.Secret);
            var observation = await FollowerDocumentPublicObservation.InvokeAsync(transport, currentCaller.Sdk,
                currentCaller.Mcp, healthy, token).ConfigureAwait(false);
            var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token).ConfigureAwait(false)).ConfigureAwait(false);
            await FollowerDocumentRf3Assertions.FullAsync(observation, healthy, state.Placement, status, status,
                RequestCqrsRf3Protocol.ChangedDocumentJson, FollowerDocumentRf3Protocol.SecondRevision,
                state.Principal.PolicyEpoch, false, upper: after);
        }
        await KeyLoad.IntegrationTests.Features.ClusterReplication.DocumentSessionReadRf3Assertions.HealthyAsync(
            currentCaller.Sdk, currentCaller.Mcp, state.Request.Reference, minimum, RequestCqrsRf3Protocol.ChangedDocumentJson,
            FollowerDocumentRf3Protocol.SecondRevision, token).ConfigureAwait(false);
    }
}
