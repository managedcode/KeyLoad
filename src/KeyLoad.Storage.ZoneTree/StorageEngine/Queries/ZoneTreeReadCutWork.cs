namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeReadCutWork(ZoneTreeReadCutLimits limits, TimeProvider clock)
{
    private const string ElapsedExceededMessage = "The execution read-cut lease exceeded its elapsed-time budget.";
    private const string RecordsExceededMessage = "The execution read-cut lease exceeded its record budget.";
    private const string BytesExceededMessage = "The execution read-cut lease exceeded its byte budget.";
    private readonly long started = clock.GetTimestamp();
    private long examinedBytes;
    private int records;
    private long advances;
    private KeyLoadException? elapsedFailure;

    internal int Records => records;
    internal long ExaminedBytes => examinedBytes;
    internal long RuntimeAdvances => advances;

    internal void Check(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CheckElapsed();
    }

    internal void CheckElapsed()
    {
        if (clock.GetElapsedTime(started) > limits.MaxElapsed)
        {
            elapsedFailure ??= Errors.Fail(ErrorCode.BudgetExceeded, ElapsedExceededMessage);
            throw elapsedFailure;
        }
    }

    internal void BeforeAdvance(CancellationToken token)
    {
        Check(token);
        advances = checked(advances + 1);
    }

    internal void ChargeExaminedKey(int keyBytes, CancellationToken token)
    {
        Check(token);
        if (keyBytes > limits.MaxExaminedBytes - examinedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, BytesExceededMessage);
        }
        examinedBytes += keyBytes;
    }

    internal void ChargeRecord(int keyBytes, int storedValueBytes, CancellationToken token)
    {
        Check(token);
        if (records >= limits.MaxRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RecordsExceededMessage);
        }

        var rowBytes = (long)keyBytes + storedValueBytes;
        if (rowBytes > limits.MaxExaminedBytes - examinedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, BytesExceededMessage);
        }

        records++;
        examinedBytes += rowBytes;
    }
}
