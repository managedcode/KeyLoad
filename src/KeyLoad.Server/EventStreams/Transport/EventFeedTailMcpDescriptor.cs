namespace KeyLoad.Server.Features.EventStreams;

internal static class EventFeedTailMcpDescriptor
{
    private const string Description = "Receive bounded original event-feed offers using the original admitted cursor. " +
        "Supply the official MCP progress token; every notification contains one complete original offer. " +
        "Cancellation ends reception without ACK or source effects. The terminal value is the last delivered original offer; " +
        "it is not another delivery or a new write receipt. Refresh, policy, coverage or owner changes terminate reception.";
    internal static McpOperationDescriptor Create() => new(EventFeedTailPublicProtocol.Tool,
        EventFeedTailPublicProtocol.Route, null, OperationKind.EventFeedControl, Description,
        McpSchemaFactory.CreateInput(typeof(EventFeedControlRequest), false),
        McpSchemaFactory.CreateOutput(typeof(EventFeedControlResult), false), new(true, true, false),
        (arguments, maximumBytes) => McpArgumentDecoder.Command<EventFeedControlRequest>(arguments,
            OperationKind.EventFeedControl, static request => EventFeedParentArguments.Require(request,
                EventFeedControlAction.Tail), maximumBytes));
}
