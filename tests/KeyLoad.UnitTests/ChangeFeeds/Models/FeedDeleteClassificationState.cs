namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed record FeedDeleteClassificationState(FeedVertexAuthorityState Source, CommandRequest Delete,
    CommitReceipt Deleted, byte[] Key, byte[] Original, string[] DirtyImage, long DirtyPosition);
