namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed class FeedAuthorityClock : TimeProvider
{
    internal static readonly DateTimeOffset Epoch = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Epoch;
}
