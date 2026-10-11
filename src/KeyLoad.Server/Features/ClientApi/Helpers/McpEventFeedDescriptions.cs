namespace KeyLoad.Server;

internal static class McpEventFeedDescriptions
{
    internal static string? Find(string name) => name switch
    {
        EventFeedOpenProtocol.Tool => EventFeedOpenProtocol.Description,
        EventFeedOfferProtocol.Tool => EventFeedOfferProtocol.Description,
        EventFeedSettlementProtocol.AcknowledgeTool => EventFeedSettlementProtocol.AcknowledgeDescription,
        EventFeedSettlementProtocol.ReleaseTool => EventFeedSettlementProtocol.ReleaseDescription,
        _ => null
    };
}
