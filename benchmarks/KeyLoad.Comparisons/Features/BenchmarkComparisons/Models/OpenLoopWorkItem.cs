namespace KeyLoad.Comparisons;

internal sealed class OpenLoopWorkItem(int index, int sampleSlot, long dueTimestamp,
    long dueOffsetNanoseconds, long deadlineTimestamp, long decisionTimestamp, int payloadBytes)
{
    internal int Index { get; } = index;
    internal int SampleSlot { get; } = sampleSlot;
    internal long DueTimestamp { get; } = dueTimestamp;
    internal long DueOffsetNanoseconds { get; } = dueOffsetNanoseconds;
    internal long DeadlineTimestamp { get; } = deadlineTimestamp;
    internal long DecisionTimestamp { get; } = decisionTimestamp;
    internal long OfferedTimestamp { get; set; }
    internal int PayloadBytes { get; } = payloadBytes;
    internal long StartedTimestamp { get; set; }
    internal int Session { get; set; } = -1;
    internal OpenLoopItemPhase Phase { get; set; }
}

internal enum OpenLoopItemPhase
{
    None,
    Queued,
    Started,
    Terminal
}
