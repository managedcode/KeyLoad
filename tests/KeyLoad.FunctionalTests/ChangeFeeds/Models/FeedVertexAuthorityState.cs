using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed record FeedVertexAuthorityState(TestDatabase Creation, CommandRequest Seed, CommitReceipt Receipt,
    DateTimeOffset CommittedAt, AstQueryRequest Query, string FeedCursor, string LiveCursor,
    byte[] Key, byte[] Original, CommandRequest? Update = null, CommitReceipt? UpdateReceipt = null);
