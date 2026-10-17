using System.Security.Cryptography;
using System.Text;
using KeyLoad.Clustering;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KeyLoad.Server.Features.EventStreams;

/// <summary>Borrows only the original official progress session inside one admitted invocation.</summary>
internal sealed class EventFeedTailMcpProgressOwner : IDisposable
{
    private const string Invalid = "The event feed Tail requires the original MCP progress token.";
    private const int NoDeliveredOffers = 0;
    private const int CurrentProgressVersion = 1;
    private readonly HttpContext context;
    private readonly RequestContext<CallToolRequestParams> request;
    private readonly ProgressToken progressToken;
    private int delivered = NoDeliveredOffers;
    private bool disposed;

    private EventFeedTailMcpProgressOwner(HttpContext context, RequestContext<CallToolRequestParams> request,
        ProgressToken progressToken)
    {
        this.context = context;
        this.request = request;
        this.progressToken = progressToken;
        if (context.Features.Get<EventFeedTailMcpProgressOwner>() is not null)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        context.Features.Set(this);
    }

    internal static EventFeedTailMcpProgressOwner? Acquire(HttpContext context,
        RequestContext<CallToolRequestParams> request, McpRequestState state)
        => state.Operation?.Name != EventFeedTailPublicProtocol.Tool ? null
            : request.Params?.ProgressToken is { } token ? new(context, request, token)
                : throw Errors.Fail(ErrorCode.Validation, Invalid);

    internal static EventFeedTailMcpProgressOwner Require(HttpContext context)
        => context.Features.Get<EventFeedTailMcpProgressOwner>() ?? throw Errors.Fail(ErrorCode.Validation, Invalid);

    internal async ValueTask SendAsync(GrainRequestProgress frame, McpRequestState state, int limit,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (frame.EventFeedTail is not { Version: CurrentProgressVersion } progress || ++delivered > limit)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var length = EventFeedTailJsonMeasurement.Measure(progress, state.MaximumReplyBytes, JsonDefaults.Options);
        using var admission = state.ReserveProgress(checked((int)length), cancellationToken);
        var bytes = McpBoundedJson.Serialize(progress, checked((int)length));
        try
        {
            await request.Server.NotifyProgressAsync(progressToken, new ProgressNotificationValue
            {
                Progress = delivered,
                Total = limit,
                Message = Encoding.UTF8.GetString(bytes)
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (ReferenceEquals(context.Features.Get<EventFeedTailMcpProgressOwner>(), this))
        {
            context.Features.Set<EventFeedTailMcpProgressOwner>(null);
        }
    }
}
