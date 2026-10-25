namespace KeyLoad.FunctionalTests.StorageReplication;

/// <summary>Explicit test-owned UTC that holds steady, or steps back on every read once descending.</summary>
/// <remarks>A wall clock may move backwards between two preparations; storage and commit execution remain real.</remarks>
internal sealed class GroupCommitClock(DateTimeOffset utcNow) : TimeProvider
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(1);
    private readonly Lock gate = new();

    internal bool Descending
    {
        get; set;
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            if (Descending)
            {
                utcNow -= Step;
            }
            return utcNow;
        }
    }
}
