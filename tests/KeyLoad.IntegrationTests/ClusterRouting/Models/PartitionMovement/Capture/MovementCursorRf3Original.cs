namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record MovementCursorRf3Original(CommandRequest Command, CommitReceipt Receipt,
    MovementCursorRf3Pages Pages, MovementCursorRf3Lives Lives, MovementCursorRf3Streams Streams);
