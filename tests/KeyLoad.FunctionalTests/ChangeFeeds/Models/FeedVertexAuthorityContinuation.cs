namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed record FeedVertexAuthorityContinuation(CommandRequest Command, CommitReceipt Receipt,
    string[] Image, long Position);
