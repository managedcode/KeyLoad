using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OwnerLossHeld
{
    private const string LostWrite = "The write response is unavailable. Retry the same command ID.";

    internal sealed record Identity(Guid ApplyId, ApplyPlacementIntentRequest Apply, Guid PersistId,
        PersistPlacementIntentRequest Persist, PlacementIntentV1 Pending);

    internal sealed record State(PlacementIntentV1 Applying, RequestCqrsProbeMarkerRecord Marker,
        RequestCqrsMovementJournalRecord Journal, ReplicaSiloDiscovery[] Discovery);

    internal static async Task<State> CaptureAsync(PartitionMovementPublicParentRf3Seed seed,
        PlacementIntentV1 pending, PartitionMoveRequest move, Guid id, ApplyPlacementIntentRequest request,
        RequestCqrsProbeFixture controls, ReplicaSiloDiscovery[] discovery,
        Task<Result<PlacementIntentV1>> original, Task<RequestCqrsProbeMarkerRecord> markerTask, CancellationToken token)
    {
        if (await Task.WhenAny(original, markerTask) == original)
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await original);
            throw new InvalidOperationException("The original Apply finished before its committed Prepare hold.");
        }
        var marker = await markerTask;
        var proof = RequestCqrsMovementJournalReader.Read(controls, marker);
        var query = new PlacementIntentReadRequest(new(request.TenantId, request.DatabaseId, request.ControlPartition),
            pending.IntentId);
        var applying = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadPlacementIntentAsync(query, token));
        await TenantCapacityPlacementRf3ClaimAssertions.ApplyingAsync(pending, applying, move, id, request,
            marker, proof, discovery);
        await Assert.That(original.IsCompleted).IsFalse();
        var refused = await seed.Official.CallAsync(TenantPlacementPublicProtocol.ApplyTool,
            new
            {
                commandId = id,
                request
            }, token);
        await McpCallerAssertions.ErrorAsync(refused, ErrorCode.Conflict, dispatched: true);
        await Assert.That(refused.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(TenantPlacementPolicyProtocol.Stale);
        await SqlRf3Protocol.EqualAsync(applying,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadPlacementIntentAsync(query, token)));
        await SqlRf3Protocol.EqualAsync(proof, RequestCqrsMovementJournalReader.Read(controls, marker));
        await Assert.That(original.IsCompleted).IsFalse();
        return new(applying, marker, proof, discovery);
    }

    internal static async Task LostReplyAsync(Result<PlacementIntentV1> result)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem).IsNotNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.UnknownWriteOutcome, LostWrite), result.Problem);
    }
}
