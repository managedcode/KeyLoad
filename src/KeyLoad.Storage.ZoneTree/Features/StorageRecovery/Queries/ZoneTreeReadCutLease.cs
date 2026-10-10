using System.Runtime.ExceptionServices;
using ZoneTree;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeReadCutLease : IDisposable
{
    private const int ValueMarkerOffset = 0;
    private const int ExaminedKeyRecordCount = 0;
    private const int VisitedRecordCount = 1;

    private const string InvalidValueHeaderMessage = "A native read-cut contains an invalid value header.";
    private readonly Action<ZoneTreeReadCutLease> release;
    private readonly CancellationTokenSource cancellation;
    private readonly ZoneTreeReadCutWork work;
    private readonly ZoneTreeReadCutLeaseState state = new();

    internal ZoneTreeReadCutLease(ZoneTreeReadCutLimits limits, Action<ZoneTreeReadCutLease> release,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        this.release = release;
        work = new(limits, timeProvider);
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    internal ZoneTreeNativeReadCut Cut => state.Cut;
    internal CancellationToken CancellationToken => cancellation.Token;
    internal void Attach(IZoneTreeIterator<Memory<byte>, Memory<byte>> acquired, ZoneTreeNativeReadCut snapshotCut)
        => state.Attach(acquired, snapshotCut);
    internal void FinishCapture() => state.FinishCapture();
    internal void CheckAfterCapture() => work.Check(CancellationToken);

    internal ZoneTreeReadCutVisitResult VisitPrefix(byte[] prefix, ZoneTreeReadCutVisitor visitor)
        => VisitPrefix(prefix, visitor, null);

    internal ZoneTreeReadCutVisitResult VisitPrefix(byte[] prefix, ZoneTreeReadCutVisitor visitor,
        Action<long, int>? admitBeforeCopy)
        => VisitPrefixCore(prefix, visitor, admitBeforeCopy, true);

    internal ZoneTreeReadCutVisitResult VisitBorrowedPrefix(byte[] prefix, ZoneTreeReadCutVisitor visitor,
        Action<long, int> admitBeforeCopy)
        => VisitPrefixCore(prefix, visitor, admitBeforeCopy, false);

    private ZoneTreeReadCutVisitResult VisitPrefixCore(byte[] prefix, ZoneTreeReadCutVisitor visitor,
        Action<long, int>? admitBeforeCopy, bool copyNativeBytes)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(visitor);
        var source = state.BeginTraversal();
        try
        {
            work.Check(CancellationToken);
            source.Seek(prefix);
            work.Check(CancellationToken);
            return Visit(source, prefix, visitor, admitBeforeCopy, copyNativeBytes);
        }
        catch (Exception failure)
        {
            state.RecordOperationFailure(failure);
            throw;
        }
        finally
        {
            state.FinishTraversal();
        }
    }

    internal bool ReadExact(byte[] key, StorageValueReader reader, Action<long, int> admitBeforeRead)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(admitBeforeRead);
        var source = state.BeginTraversal();
        try
        {
            work.ChargeExaminedKey(key.Length, CancellationToken);
            admitBeforeRead(key.Length, ExaminedKeyRecordCount);
            source.Seek(key);
            work.BeforeAdvance(CancellationToken);
            var found = source.Next();
            work.Check(CancellationToken);
            if (!found)
            { return false; }
            if (!source.CurrentKey.Span.SequenceEqual(key))
            {
                work.ChargeExaminedKey(source.CurrentKey.Length, CancellationToken);
                admitBeforeRead(source.CurrentKey.Length, ExaminedKeyRecordCount);
                return false;
            }
            ReadExactValue(source, reader, admitBeforeRead);
            return true;
        }
        catch (Exception failure)
        {
            state.RecordOperationFailure(failure);
            throw;
        }
        finally
        {
            state.FinishTraversal();
        }
    }

    private void ReadExactValue(IZoneTreeIterator<Memory<byte>, Memory<byte>> source,
        StorageValueReader reader, Action<long, int> admitBeforeRead)
    {
        var raw = source.CurrentValue;
        work.ChargeRecord(source.CurrentKey.Length, raw.Length, CancellationToken);
        if (raw.IsEmpty || raw.Span[ValueMarkerOffset] != LiveValueMarker)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidValueHeaderMessage); }
        admitBeforeRead((long)source.CurrentKey.Length + raw.Length, VisitedRecordCount);
        reader(raw.Span[StorageValueHeaderBytes..]);
        work.Check(CancellationToken);
    }

    internal ZoneTreeReadCutVisitResult ObserveSettledWork()
    {
        state.EnsureTraversalSettled();
        return Result(stoppedByVisitor: false);
    }

    internal void EnsureDisposalCanJoin() => state.EnsureDisposalCanJoin();
    public void Dispose()
    {
        var attempt = state.BeginDispose();
        if (!attempt.Owner)
        {
            attempt.Completion.GetAwaiter().GetResult();
            return;
        }

        var failures = new List<Exception>();
        if (ZoneTreeReadCutDisposal.SettleNative(this, attempt.Capture, attempt.Traversal, failures)
            && ZoneTreeReadCutDisposal.TryCleanup(ReleaseSlot, failures))
        {
            try
            {
                cancellation.Dispose();
                state.MarkFinalized();
                work.CheckElapsed();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
                CompleteDisposal(failures);
                throw;
            }
        }
        CompleteDisposal(failures);
    }

    internal IZoneTreeIterator<Memory<byte>, Memory<byte>>? CurrentIterator() => state.CurrentIterator();
    internal bool MarkIteratorDisposed(IZoneTreeIterator<Memory<byte>, Memory<byte>> iterator)
        => state.MarkIteratorDisposed(iterator);
    internal void CancelLifetime() => cancellation.Cancel();
    internal void CompleteDisposal(List<Exception> failures)
    {
        var failure = state.CompleteDisposeAttempt(failures);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void ReleaseSlot() => release(this);

    private ZoneTreeReadCutVisitResult Visit(IZoneTreeIterator<Memory<byte>, Memory<byte>> source,
        byte[] prefix, ZoneTreeReadCutVisitor visitor, Action<long, int>? admitBeforeCopy, bool copyNativeBytes)
    {
        while (true)
        {
            work.BeforeAdvance(CancellationToken);
            var found = source.Next();
            work.Check(CancellationToken);
            if (!found)
            {
                break;
            }

            var key = source.CurrentKey;
            if (!key.Span.StartsWith(prefix))
            {
                work.ChargeExaminedKey(key.Length, CancellationToken);
                admitBeforeCopy?.Invoke(key.Length, ExaminedKeyRecordCount);
                break;
            }

            var rawValue = source.CurrentValue;
            work.ChargeRecord(key.Length, rawValue.Length, CancellationToken);
            if (rawValue.IsEmpty || rawValue.Span[ValueMarkerOffset] != LiveValueMarker)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidValueHeaderMessage);
            }
            admitBeforeCopy?.Invoke((long)key.Length + rawValue.Length, VisitedRecordCount);

            var keepGoing = copyNativeBytes
                ? visitor(key.ToArray(), rawValue.Span[StorageValueHeaderBytes..].ToArray())
                : visitor(key.Span, rawValue.Span[StorageValueHeaderBytes..]);
            work.Check(CancellationToken);
            if (!keepGoing)
            {
                return Result(stoppedByVisitor: true);
            }
        }

        work.Check(CancellationToken);
        return Result(stoppedByVisitor: false);
    }

    private ZoneTreeReadCutVisitResult Result(bool stoppedByVisitor)
        => new(work.Records, work.ExaminedBytes, work.NativeAdvances, stoppedByVisitor, stoppedByVisitor);
}
