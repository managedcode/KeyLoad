namespace KeyLoad.Comparisons.Targets;

internal readonly record struct KurrentCleanupCounts(int Tracked, int Submitted, int Acknowledged, int Faulted, int Pending, int PeakConcurrency)
{
    private const int NoObservedItems = 0;
    private const int MaximumEncodedCleanupConcurrency = 16;

    internal bool IsValid => Tracked >= NoObservedItems && Submitted >= NoObservedItems && Submitted <= Tracked && Acknowledged >= NoObservedItems && Faulted >= NoObservedItems && Pending >= NoObservedItems
        && (long)Acknowledged + Faulted + Pending == Submitted && Pending <= MaximumEncodedCleanupConcurrency
        && PeakConcurrency >= Pending && PeakConcurrency <= MaximumEncodedCleanupConcurrency && PeakConcurrency <= Submitted
        && (Submitted == NoObservedItems ? PeakConcurrency == NoObservedItems : PeakConcurrency > NoObservedItems);

    internal bool IsComplete => IsValid && Submitted == Tracked && Acknowledged == Tracked && Faulted == NoObservedItems && Pending == NoObservedItems;
}
