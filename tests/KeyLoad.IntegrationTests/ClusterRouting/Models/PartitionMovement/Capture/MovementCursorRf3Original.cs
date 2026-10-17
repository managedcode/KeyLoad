namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record MovementCursorRf3Pages(QueryPage Source, QueryPage Target, QueryPage Official);
internal sealed record MovementCursorRf3Lives(LiveQuerySnapshot Source, LiveQuerySnapshot Target, LiveQuerySnapshot Official);
internal sealed record MovementCursorRf3Streams(StreamPage Source, StreamPage Target, StreamPage Official);
internal sealed record MovementCursorRf3Original(CommandRequest Command, CommitReceipt Receipt,
    MovementCursorRf3Pages Pages, MovementCursorRf3Lives Lives, MovementCursorRf3Streams Streams);
