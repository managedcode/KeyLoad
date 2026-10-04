using System.Diagnostics;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeReadCutWork(ZoneTreeReadCutLimits limits)
{
    private const string ElapsedExceededMessage = "The native read-cut lease exceeded its elapsed-time budget.";
    private const string RecordsExceededMessage = "The native read-cut lease exceeded its record budget.";
    private const string BytesExceededMessage = "The native read-cut lease exceeded its byte budget.";
    private readonly long started = Stopwatch.GetTimestamp();
    private long examinedBytes;
    private int records;
    private long advances;
    private KeyLoadException? elapsedFailure;

    internal int Records => records;
    internal long ExaminedBytes => examinedBytes;
    internal long NativeAdvances => advances;

    internal void Check(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CheckElapsed();
    }

    internal void CheckElapsed()
    {
        if (Stopwatch.GetElapsedTime(started) > limits.MaxElapsed)
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
