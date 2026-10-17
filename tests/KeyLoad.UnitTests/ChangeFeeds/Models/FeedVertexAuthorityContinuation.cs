namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record FeedVertexAuthorityContinuation(CommandRequest Command, CommitReceipt Receipt,
    string[] Image, long Position);
