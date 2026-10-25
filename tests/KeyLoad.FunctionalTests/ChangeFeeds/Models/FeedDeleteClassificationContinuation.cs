namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed record FeedDeleteClassificationContinuation(FeedDeleteClassificationState State,
    CommandRequest Resurrection, CommitReceipt Restored, string[] Image, long Position);
