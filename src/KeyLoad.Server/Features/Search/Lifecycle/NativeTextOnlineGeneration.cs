using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;
using ZoneTree;
using ZoneTree.FullTextSearch;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOnlineGeneration(string root, OnlineTextCurrentPublication original,
    NativeTextIncrementalManifest manifest, IOptions<NativeTextExecutionOptions> options, NativeTextResourceOwnership resources) : INativeTextSelectedIndexLeaseOwner, IDisposable
{
    private const int SingleFailure = 1;
    private const int NoSettledFailures = 0;
    private const int NoReaderPins = 0;
    private readonly Lock gate = new();
    private readonly NativeTextSelectedIndexSlot index = new(root, original.Authority.Leaf,
        original.PublishedCut.NodeId, options, resources);
    private TaskCompletionSource? quiet;
    private int pins;
    private bool retiring;
    private bool retired;
    private Dictionary<NativeTextSelectedProjectionLease, (NativeTextOnlineGenerationPin Pin, NativeTextResourceReservation Grant, bool IndexExited)>? readers;
    private Exception? readerFailure;
    private NativeTextResourceReservation? reservation;

    public void Enter(ReadExecutionBudget budget, NativeTextSelectedProjectionLease reader)
    {
        var grant = resources.ReserveLease(budget);
        NativeTextOnlineGenerationPin? retained = null;
        var transferred = false;
        var failures = new List<Exception>();
        try
        {
            try
            {
                retained = Pin(budget);
                lock (gate)
                {
                    AdmitReaderLedger(budget, reader);
                    readers!.Add(reader, (retained, grant, false));
                    retained = null;
                    transferred = true;
                    index.Enter(budget, reader);
                }
            }
            catch (Exception primary)
            {
                failures.Add(primary);
                if (transferred)
                { SettleFailedReader(primary, reader, failures); }
                throw;
            }
            finally { retained?.Dispose(); }
        }
        catch (Exception cleanup)
        {
            if (!failures.Any(originalFailure => ReferenceEquals(originalFailure, cleanup)))
            { failures.Add(cleanup); }
            if (!transferred && failures.Count == SingleFailure)
            { ServerFailureObserver.Observe(grant.CompleteAfterJoinedCleanup, failures); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private void AdmitReaderLedger(ReadExecutionBudget budget, NativeTextSelectedProjectionLease reader)
        => readers = NativeTextOnlineReaderLedger.Admit(readers, budget, reader, options);

    private void SettleFailedReader(Exception primary, NativeTextSelectedProjectionLease reader, List<Exception> failures)
    {
        lock (gate)
        {
            var settled = new List<Exception>();
            ServerFailureObserver.Observe(() => index.RequireReaderSettled(reader), settled);
            if (settled.Count != NoSettledFailures)
            {
                RetainReaderFailure(primary);
                failures.AddRange(settled);
                foreach (var cleanup in settled)
                { RetainReaderFailure(cleanup); }
                return;
            }
            ServerFailureObserver.Observe(() => CompleteReader(reader, primary), failures);
        }
    }

    private void CompleteReader(NativeTextSelectedProjectionLease reader, Exception? initiating = null)
    {
        var original = readers![reader];
        try
        {
            original.Pin.Dispose();
            original.Grant.CompleteAfterJoinedCleanup();
            _ = readers.Remove(reader);
        }
        catch (Exception cleanup)
        {
            if (initiating is not null)
            { RetainReaderFailure(initiating); }
            RetainReaderFailure(cleanup);
            throw;
        }
    }

    public IZoneTreeIterator<CompositeKeyOfTokenRecordPrevious<ulong, ulong>, byte>
        CreateIterator() => index.CreateIterator();

    public void ObserveOriginalPostingRead() => index.ObserveOriginalPostingRead();

    public void Exit(NativeTextSelectedProjectionLease reader)
    {
        lock (gate)
        {
            if (readers is null || !readers.TryGetValue(reader, out var original))
            { throw NativeTextErrors.Ownership(); }
            try
            {
                if (!original.IndexExited)
                {
                    index.Exit(reader);
                    readers[reader] = (original.Pin, original.Grant, true);
                }
            }
            catch (Exception primary) { RetainReaderFailure(primary); throw; }
            CompleteReader(reader);
        }
    }

    private void RetainReaderFailure(Exception original)
    {
        readerFailure = readerFailure is null ? original : new AggregateException(readerFailure, original);
        quiet?.TrySetException(readerFailure);
    }

    internal string Root => root;
    internal OnlineTextCurrentPublication Original => original;
    internal NativeTextIncrementalManifest Manifest => manifest;

    internal NativeTextOnlineGenerationPin Pin(ReadExecutionBudget budget)
    {
        lock (gate)
        {
            budget.Check();
            if (retiring || retired)
            { throw NativeTextErrors.Ownership(); }
            pins++;
            return new(this);
        }
    }

    internal void RequireActive()
    {
        lock (gate)
        {
            if (retired || pins <= NoReaderPins)
            { throw NativeTextErrors.Ownership(); }
        }
    }

    internal void ReleasePin()
    {
        lock (gate)
        {
            if (pins <= NoReaderPins)
            { throw NativeTextErrors.Ownership(); }
            pins--;
            if (pins == NoReaderPins)
            { quiet?.TrySetResult(); }
        }
    }

    internal void AdoptReservation(NativeTextResourceReservation? originalReservation)
    {
        lock (gate)
        {
            if (reservation is not null || retiring || retired)
            { throw NativeTextErrors.Ownership(); }
            reservation = originalReservation;
        }
    }

    internal void ReleaseAfterActualDirectoryDeletion()
    {
        lock (gate)
        {
            if (!retired || pins != NoReaderPins)
            { throw NativeTextErrors.Ownership(); }
            reservation?.CompleteAfterJoinedCleanup();
            reservation = null;
        }
    }

    internal Task MarkRetirement()
    {
        lock (gate)
        {
            retiring = true;
            if (readerFailure is not null)
            { return Task.FromException(readerFailure); }
            if (pins == NoReaderPins)
            { return Task.CompletedTask; }
            quiet ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            return quiet.Task;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (retired)
            { return; }
            if (readerFailure is not null)
            { throw new AggregateException(readerFailure); }
            if (!retiring || pins != NoReaderPins || (readers?.Count ?? NoReaderPins) != NoReaderPins)
            { throw NativeTextErrors.Ownership(); }
            index.RequireSettled();
            index.Dispose();
            retired = true;
        }
    }

    internal void CompleteAfterJoinedRetirement() => Dispose();
}
