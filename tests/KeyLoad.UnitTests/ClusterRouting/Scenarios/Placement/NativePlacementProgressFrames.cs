using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Captures real native CQRS writer frames; these controls do not execute a placement effect.</summary>
internal static class NativePlacementProgressFrames
{
    internal static async Task<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>[]> CreateAsync(
        Guid requestId, params PlacementIntentProgressV1[] observations)
    {
        var frames = new List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>();
        var stream = CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(async writer =>
        {
            await writer.StartedAsync(new GrainRequestProgress(requestId));
            foreach (var observation in observations)
            {
                await writer.ProgressAsync(new GrainRequestProgress(requestId)
                {
                    PlacementIntent = observation
                }, PlacementIntentNativeProtocol.Progress);
            }
            return Result<GrainOperationReply>.Succeed(new GrainOperationReply());
        }, TestContext.Current!.Execution.CancellationToken);
        await foreach (var frame in stream)
        {
            frames.Add(frame);
        }
        return frames.Where(frame => frame.Kind == CqrsStreamChunkKind.Progress).ToArray();
    }
}
