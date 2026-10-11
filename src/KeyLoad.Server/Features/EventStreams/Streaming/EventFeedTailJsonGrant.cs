using System.Text.Json;
using KeyLoad.Orleans;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

/// <summary>Reserves one actually measured typed terminal inside the original JSON and frame grants.</summary>
internal sealed class EventFeedTailJsonGrant
{
    private const int EmptyFrames = 0;
    private const int TerminalFrames = 1;
    private const long EmptyBytes = 0;
    private const string Exhausted = "The event feed public stream exceeds its configured JSON grant.";
    private readonly int frameLimit;
    private readonly long totalLimit;
    private readonly int maximumFrames;
    private readonly JsonSerializerOptions serializer;
    private readonly long terminalBytes;
    private long bytes = EmptyBytes;
    private int frames = EmptyFrames;

    private EventFeedTailJsonGrant(int frameLimit, long totalLimit, int maximumFrames, JsonSerializerOptions serializer)
    {
        this.frameLimit = frameLimit;
        this.totalLimit = totalLimit;
        this.maximumFrames = maximumFrames;
        this.serializer = serializer;
        terminalBytes = EventFeedTailJsonMeasurement.Measure(Failure(maximumFrames), frameLimit, serializer);
        if (maximumFrames < TerminalFrames || terminalBytes > totalLimit)
        { throw Refusal(); }
    }

    internal static EventFeedTailJsonGrant Create(HttpContext context)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<DatabaseLimits>>().Value;
        var routing = context.RequestServices.GetRequiredService<IOptions<GrainRoutingOptions>>().Value;
        var mcp = context.RequestServices.GetRequiredService<IOptions<McpExecutionOptions>>().Value;
        var options = context.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value;
        limits.Validate();
        routing.Validate();
        mcp.Validate();
        return new EventFeedTailJsonGrant(checked((int)Math.Min(limits.MaxQueryReadBytes,
            Math.Min(routing.MaximumCompletedBytes, mcp.MaximumDataReplyBytes))),
            Math.Min(limits.MaxQueryReadBytes, routing.MaximumAggregateBytes), routing.MaximumTotalFrames,
            options.SerializerOptions);
    }

    internal void Admit(CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult> frame)
    {
        var reservedFrames = frame.IsTerminal ? EmptyFrames : TerminalFrames;
        var reservedBytes = frame.IsTerminal ? EmptyBytes : terminalBytes;
        if (frames >= maximumFrames - reservedFrames)
        { throw Refusal(); }
        var measured = EventFeedTailJsonMeasurement.Measure(frame, frameLimit, serializer);
        if (measured > totalLimit - bytes - reservedBytes)
        { throw Refusal(); }
        bytes += measured;
        frames++;
    }

    internal CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult> AdmitFailure()
    {
        var terminal = Failure(checked(frames + TerminalFrames));
        Admit(terminal);
        return terminal;
    }

    private static CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult> Failure(long sequence)
        => CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult>.Failed(
            Result<EventFeedControlResult>.Fail(Errors.Problem(ErrorCode.ResourceExhausted, Exhausted)), sequence: sequence);

    private static KeyLoadException Refusal() => Errors.Fail(ErrorCode.ResourceExhausted, Exhausted);
}
