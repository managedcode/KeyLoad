using System.Runtime.ExceptionServices;
using ZoneTree;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeReadCutLease : IDisposable
{
    private const int ValueMarkerOffset = 0;

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
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(visitor);
        var source = state.BeginTraversal();
        try
        {
            work.Check(CancellationToken);
            source.Seek(prefix);
            work.Check(CancellationToken);
            return Visit(source, prefix, visitor);
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
        byte[] prefix, ZoneTreeReadCutVisitor visitor)
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
                break;
            }

            var rawValue = source.CurrentValue;
            work.ChargeRecord(key.Length, rawValue.Length, CancellationToken);
            if (rawValue.IsEmpty || rawValue.Span[ValueMarkerOffset] != LiveValueMarker)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidValueHeaderMessage);
            }

            var ownedKey = key.ToArray();
            var ownedValue = rawValue.Span[StorageValueHeaderBytes..].ToArray();
            var keepGoing = visitor(ownedKey, ownedValue);
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
