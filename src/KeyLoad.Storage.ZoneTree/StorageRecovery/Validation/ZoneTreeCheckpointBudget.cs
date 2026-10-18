namespace KeyLoad.Storage.ZoneTree;

/// <summary>One cumulative ledger for the original image and every bounded verification cursor.</summary>
internal sealed class ZoneTreeCheckpointBudget(ZoneTreeStoreOptions options, TimeProvider clock, CancellationToken token)
{
    private const long Empty = 0;
    private const long OneRecord = 1;
    private const string Exhausted = "The native checkpoint exceeds its original read budget.";
    private readonly long started = clock.GetTimestamp();
    private long bytes = Empty;
    private long records = Empty;

    internal void Check()
    {
        token.ThrowIfCancellationRequested();
        if (clock.GetElapsedTime(started) > options.MaximumReadCutElapsed)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Exhausted);
        }
    }

    internal void Charge(long byteCount, long recordCount = OneRecord)
    {
        Check();
        if (byteCount < Empty || recordCount < Empty
            || byteCount > options.MaximumReadCutExaminedBytes - bytes
            || recordCount > options.MaximumReadCutRecords - records)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Exhausted);
        }
        bytes += byteCount;
        records += recordCount;
    }

    internal ZoneTreeCheckpointFrame ReadFrame(FileStream input)
    {
        Check();
        var remaining = options.MaximumReadCutExaminedBytes - bytes;
        var maximum = input.Position > long.MaxValue - remaining ? long.MaxValue : input.Position + remaining;
        var frame = ZoneTreeCheckpointFrame.Read(input, options, maximum);
        Charge(checked((long)frame.Header.Length + frame.Payload.Length), Empty);
        return frame;
    }
}
