using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Uses the production progress writer and real bounded CQRS producer, without a mirror shape validator.</summary>
internal static class NativeAnnSettledProgressFrames
{
    private const int OneFinal = 1;
    internal static async Task<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>[]> CreateAsync(
        Guid requestId, AnnMaintenancePhase[] phases)
    {
        var frames = new List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>();
        var stream = CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(async writer =>
        {
            await writer.StartedAsync(new GrainRequestProgress(requestId));
            foreach (var phase in phases)
            {
                await AnnMaintenanceProgress.WriteAsync(writer, requestId, phase);
            }
            return Result<GrainOperationReply>.Succeed(new GrainOperationReply());
        }, TestContext.Current!.Execution.CancellationToken);
        await foreach (var frame in stream)
        {
            frames.Add(frame);
        }
        await Assert.That(frames.Count(value => value.Kind == CqrsStreamChunkKind.Completed)).IsEqualTo(OneFinal);
        return frames.Where(value => value.Kind == CqrsStreamChunkKind.Progress).ToArray();
    }
}
