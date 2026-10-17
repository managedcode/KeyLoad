using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record FeedVertexAuthorityState(CommandRequest Seed, CommitReceipt Receipt,
    DateTimeOffset CommittedAt, AstQueryRequest Query, string FeedCursor, string LiveCursor,
    byte[] Key, byte[] Original, CommandRequest? Update = null, CommitReceipt? UpdateReceipt = null);
