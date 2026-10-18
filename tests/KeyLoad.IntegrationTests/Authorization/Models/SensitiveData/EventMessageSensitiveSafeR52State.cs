namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed class EventMessageSensitiveSafeR52State(EventMessageSensitivePublicState model)
{
    internal EventMessageSensitivePublicState Model { get; } = model;
    internal OutboxStatus Outbox { get; set; } = null!;
    internal EventSourcePage? Page
    {
        get; set;
    }
    internal Dictionary<string, MessageInspection?> Messages { get; } = [];
    internal List<string> Published { get; } = [];
    internal HashSet<string> Leased { get; } = [];
    internal List<string> Acknowledged { get; } = [];
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Producers { get; } = [];
    internal List<(object Command, CommitReceipt Receipt)> Completions { get; } = [];
    internal object OriginalRequest { get; set; } = null!;
    internal object Original { get; set; } = null!;
    internal string FreshId => Model.Subscription ? EventMessageSensitiveSafeR52Protocol.First : EventMessageSensitiveSafeR52Protocol.Second;
    internal object FreshRequest { get; set; } = null!;
    internal object Fresh { get; set; } = null!;
}
