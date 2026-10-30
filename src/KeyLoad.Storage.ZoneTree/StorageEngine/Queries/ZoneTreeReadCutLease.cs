using ZoneTree.Transactional;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeReadCutLease : IDisposable
{
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

    internal ZoneTreeReadCut Cut => state.Cut;
    internal CancellationToken CancellationToken => cancellation.Token;
    internal void Attach(ZoneTreeSnapshotCursor acquired, ZoneTreeReadCut snapshotCut)
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
        Action<long, int>? admitBeforeCopy, bool copyBytes)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(visitor);
        var source = state.BeginTraversal();
        try
        {
            work.Check(CancellationToken);
            source.Seek(prefix);
            work.Check(CancellationToken);
            return Visit(source, prefix, visitor, admitBeforeCopy, copyBytes);
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
            admitBeforeRead(key.Length, 0);
            source.Seek(key);
            work.BeforeAdvance(CancellationToken);
            var found = source.Next();
            work.Check(CancellationToken);
            if (!found)
            {
                return false;
            }
            if (!source.CurrentKey.Span.SequenceEqual(key))
            {
                work.ChargeExaminedKey(source.CurrentKey.Length, CancellationToken);
                admitBeforeRead(source.CurrentKey.Length, 0);
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

    private void ReadExactValue(ITransactionIterator<Memory<byte>, Memory<byte>> source,
        StorageValueReader reader, Action<long, int> admitBeforeRead)
    {
        var raw = source.CurrentValue;
        work.ChargeRecord(source.CurrentKey.Length, raw.Length, CancellationToken);
        admitBeforeRead((long)source.CurrentKey.Length + raw.Length, 1);
        reader(raw.Span);
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
        if (ZoneTreeReadCutDisposal.RuntimeSettle(this, attempt.Capture, attempt.Traversal, failures)
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

    internal ZoneTreeSnapshotCursor? CurrentSnapshot() => state.CurrentSnapshot();
    internal bool MarkSnapshotDisposed(ZoneTreeSnapshotCursor snapshot)
        => state.MarkSnapshotDisposed(snapshot);
    internal void CancelLifetime() => cancellation.Cancel();
    internal void CompleteDisposal(List<Exception> failures)
        => state.CompleteDisposeAttempt(failures).GetAwaiter().GetResult();

    private void ReleaseSlot() => release(this);

    private ZoneTreeReadCutVisitResult Visit(ITransactionIterator<Memory<byte>, Memory<byte>> source,
        byte[] prefix, ZoneTreeReadCutVisitor visitor, Action<long, int>? admitBeforeCopy, bool copyBytes)
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
                admitBeforeCopy?.Invoke(key.Length, 0);
                break;
            }

            var rawValue = source.CurrentValue;
            work.ChargeRecord(key.Length, rawValue.Length, CancellationToken);
            admitBeforeCopy?.Invoke((long)key.Length + rawValue.Length, 1);

            var keepGoing = copyBytes
                ? visitor(key.ToArray(), rawValue.ToArray())
                : visitor(key.Span, rawValue.Span);
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
        => new(work.Records, work.ExaminedBytes, work.RuntimeAdvances, stoppedByVisitor, stoppedByVisitor);
}
