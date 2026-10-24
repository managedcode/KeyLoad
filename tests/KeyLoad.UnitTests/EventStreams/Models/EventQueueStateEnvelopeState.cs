namespace KeyLoad.UnitTests.EventStreams;

internal sealed class EventQueueStateEnvelopeState
{
    internal List<EventQueueStateEnvelopeHistory> History { get; } = [];
    internal Dictionary<string, byte[]> Bodies { get; } = [];
    internal Dictionary<string, byte[]> SourceRows { get; } = [];
    internal byte[] GraphBytes { get; set; } = [];
    internal byte[] InboxBytes { get; set; } = [];
    internal ReceiveResult? ReadyClaim
    {
        get; set;
    }
    internal ReceiveResult? ScheduledClaim
    {
        get; set;
    }
    internal ReceiveSubscriptionResult? GroupClaim
    {
        get; set;
    }
    internal ProcessingRequest? Processing
    {
        get; set;
    }
    internal CommitReceipt? ProcessingReceipt
    {
        get; set;
    }
}
