using KeyLoad.Core;
using KeyLoad.Core.Search;
using Microsoft.Extensions.Options;
using ZoneTree;
using ZoneTree.FullTextSearch;

namespace KeyLoad.Server.Search;

internal sealed class TextOnlineGeneration(string root, OnlineTextCurrentPublication original,
    TextIncrementalManifest manifest, IOptions<TextExecutionOptions> options, TextResourceOwnership resources) : ITextSelectedIndexLeaseOwner, IDisposable
{
    private const int SingleFailure = 1;
    private const int NoSettledFailures = 0;
    private const int NoReaderPins = 0;
    private readonly Lock gate = new();
    private readonly TextSelectedIndexSlot index = new(root, original.Authority.Leaf,
        original.PublishedCut.NodeId, options, resources);
    private TaskCompletionSource? quiet;
    private int pins;
    private bool retiring;
    private bool retired;
    private Dictionary<TextSelectedProjectionLease, (TextOnlineGenerationPin Pin, TextResourceReservation Grant, bool IndexExited)>? readers;
    private Exception? readerFailure;
    private TextResourceReservation? reservation;

    public void Enter(ReadExecutionBudget budget, TextSelectedProjectionLease reader)
    {
        var grant = resources.ReserveLease(budget);
        TextOnlineGenerationPin? retained = null;
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
                {
                    SettleFailedReader(primary, reader, failures);
                }
                throw;
            }
            finally
            {
                retained?.Dispose();
            }
        }
        catch (Exception cleanup)
        {
            if (!failures.Any(originalFailure => ReferenceEquals(originalFailure, cleanup)))
            {
                failures.Add(cleanup);
            }
            if (!transferred && failures.Count == SingleFailure)
            {
                ServerFailureObserver.Observe(grant.CompleteAfterJoinedCleanup, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private void AdmitReaderLedger(ReadExecutionBudget budget, TextSelectedProjectionLease reader)
        => readers = TextOnlineReaderLedger.Admit(readers, budget, reader, options);

    private void SettleFailedReader(Exception primary, TextSelectedProjectionLease reader, List<Exception> failures)
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
                {
                    RetainReaderFailure(cleanup);
                }
                return;
            }
            ServerFailureObserver.Observe(() => CompleteReader(reader, primary), failures);
        }
    }

    private void CompleteReader(TextSelectedProjectionLease reader, Exception? initiating = null)
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
            {
                RetainReaderFailure(initiating);
            }
            RetainReaderFailure(cleanup);
            throw;
        }
    }

    public IZoneTreeIterator<CompositeKeyOfTokenRecordPrevious<ulong, ulong>, byte>
        CreateIterator() => index.CreateIterator();

    public void ObserveOriginalPostingRead() => index.ObserveOriginalPostingRead();

    public void Exit(TextSelectedProjectionLease reader)
    {
        lock (gate)
        {
            if (readers is null || !readers.TryGetValue(reader, out var original))
            {
                throw TextErrors.Ownership();
            }
            try
            {
                if (!original.IndexExited)
                {
                    index.Exit(reader);
                    readers[reader] = (original.Pin, original.Grant, true);
                }
            }
            catch (Exception primary)
            {
                RetainReaderFailure(primary);
                throw;
            }
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
    internal TextIncrementalManifest Manifest => manifest;

    internal TextOnlineGenerationPin Pin(ReadExecutionBudget budget)
    {
        lock (gate)
        {
            return TextGenerationRetirement.Pin(this, budget, ref retiring, ref retired, ref pins);
        }
    }

    internal void RequireActive()
    {
        lock (gate)
        {
            if (retired || pins <= NoReaderPins)
            {
                throw TextErrors.Ownership();
            }
        }
    }

    internal void ReleasePin()
    {
        lock (gate)
        {
            TextGenerationRetirement.ReleasePin(ref pins, ref quiet);
        }
    }

    internal void AdoptReservation(TextResourceReservation? originalReservation)
    {
        lock (gate)
        {
            TextGenerationRetirement.Adopt(originalReservation, retiring, retired, ref reservation);
        }
    }

    internal void ReleaseAfterActualDirectoryDeletion()
    {
        lock (gate)
        {
            TextGenerationRetirement.Release(retired, pins, ref reservation);
        }
    }

    internal Task MarkRetirement()
    {
        lock (gate)
        {
            return TextGenerationRetirement.Mark(readerFailure, pins, ref retiring, ref quiet);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            TextGenerationRetirement.Dispose(index, readerFailure, retiring, pins, readers?.Count ?? NoReaderPins, ref retired);
        }
    }

    internal void CompleteAfterJoinedRetirement() => Dispose();
}
