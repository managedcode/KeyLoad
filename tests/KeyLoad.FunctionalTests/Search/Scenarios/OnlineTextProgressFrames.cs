using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests.Search;

/// <summary>Collects actual production-writer frames from the real bounded CQRS producer; executes no index effects.</summary>
internal static class OnlineTextProgressFrames
{
    internal const int StartedSequence = 1;
    internal const int FirstProgressSequence = 2;
    internal const int First = 0;
    internal const int Step = 1;
    private const byte ControlPayload = 1;
    internal static readonly OnlineTextPhase[] Grammar =
        [OnlineTextPhase.Captured, OnlineTextPhase.Seeded, OnlineTextPhase.CaughtUp,
            OnlineTextPhase.Validated, OnlineTextPhase.Published, OnlineTextPhase.RetirementMarked];

    internal static async Task<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>[]> CreateAsync(
        Guid requestId, OnlineTextPhase[] phases)
    {
        var options = new GrainRoutingOptions();
        options.Validate();
        var accounting = new OnlineTextFrameAccounting(Options.Create(options));
        var stream = CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(async writer =>
        {
            await writer.StartedAsync(new GrainRequestProgress(requestId));
            foreach (var phase in phases)
            {
                await OnlineTextProgress.WriteAsync(writer, accounting, requestId, phase);
            }
            // Opaque terminal bytes are only a execution CQRS grammar control, never an online maintenance result.
            return Result<GrainOperationReply>.Succeed(new GrainOperationReply { Payload = new byte[] { ControlPayload } });
        }, TestContext.Current!.Execution.CancellationToken);
        var frames = new List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>();
        await foreach (var frame in stream)
        {
            frames.Add(frame);
        }
        await Assert.That(frames.Count(frame => frame.Kind == CqrsStreamChunkKind.Completed)).IsEqualTo(1);
        await Assert.That(frames.Count(frame => frame.Kind == CqrsStreamChunkKind.Failed)).IsEqualTo(First);
        return [.. frames];
    }
}
