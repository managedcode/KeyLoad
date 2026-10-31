using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal sealed class TextSelectedProjectionLease : ITextProjectionLease, IOriginalTextPostingObservation
{
    private readonly ITextSelectedIndexLeaseOwner slot;
    private readonly TextSelectedReadLease admission;
    private readonly ReadExecutionBudget budget;
    private readonly HashSet<EntityRef> visited = [];
    private readonly Dictionary<EntityRef, TextIncrementalRecord> byReference;
    private readonly Dictionary<ulong, TextIncrementalRecord> byId;
    private readonly bool entered;
    private bool completed;
    private bool disposed;
    private Func<CancellationToken, ValueTask>? postingObservation;
    private bool postingObserved;

    internal TextSelectedProjectionLease(TextSelectedReadAdmission admissionOwner,
        Func<(ITextSelectedIndexLeaseOwner Slot, TextIncrementalManifest Manifest)> capture,
        ReadExecutionBudget budget)
    {
        this.budget = budget;
        try
        {
            admission = admissionOwner.EnterRead();
            var selected = capture();
            slot = selected.Slot;
            slot.Enter(budget, this);
            entered = true;
            byReference = TextSelectedRecordMap.ByReference(selected.Manifest, budget);
            byId = TextSelectedRecordMap.ById(selected.Manifest, budget);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            {
                Dispose();
            }
            catch (Exception cleanup) when (CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            catch (Exception cleanup) when (!CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public void ObserveOriginalPosting(Func<CancellationToken, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (disposed || completed || postingObservation is not null)
        {
            throw TextErrors.Corrupt();
        }
        postingObservation = callback;
    }

    private void ObserveOriginalPosting()
    {
        var callback = postingObservation;
        if (callback is null || postingObserved)
        {
            return;
        }
        postingObserved = true;
        var failures = new List<Exception>();
        ServerFailureObserver.ObserveAsync(() => callback(budget.Cancellation).AsTask(), failures).GetAwaiter().GetResult();
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void BeginRecord(EntityRef reference, long revision)
    {
        budget.Check();
        if (disposed || completed || !visited.Add(reference))
        {
            throw TextErrors.Corrupt();
        }
        if (!byReference.TryGetValue(reference, out var record) || record.Deleted || record.Revision != revision)
        {
            throw TextErrors.Mismatch();
        }
    }

    public void ObserveToken(string token)
    {
        budget.Check();
        if (disposed || completed || string.IsNullOrEmpty(token))
        {
            throw TextErrors.Corrupt();
        }
    }

    public void VerifyCandidates(IReadOnlyList<string> terms, IReadOnlyList<EntityRef> references,
        ReadExecutionBudget verificationBudget)
    {
        if (disposed || completed || verificationBudget != budget)
        {
            throw TextErrors.Corrupt();
        }
        var candidates = new HashSet<EntityRef>();
        foreach (var term in terms)
        {
            ReadCandidates(TextHash.Sha256(term), candidates);
        }
        foreach (var reference in references)
        {
            budget.Check();
            if (!visited.Contains(reference) || !candidates.Contains(reference))
            {
                throw TextErrors.Corrupt();
            }
        }
        budget.Check();
        completed = true;
    }

    private void ReadCandidates(ulong token, HashSet<EntityRef> candidates)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            using var iterator = slot.CreateIterator();
            ServerFailureObserver.Observe(() =>
            {
                iterator.Seek(RuntimeTextIndex.LowerBound(token));
                while (true)
                {
                    if (!iterator.Next())
                    {
                        return;
                    }
                    slot.ObserveOriginalPostingRead();
                    ObserveOriginalPosting();
                    budget.Check();
                    budget.Cancellation.ThrowIfCancellationRequested();
                    if (iterator.CurrentKey.Token != token)
                    {
                        return;
                    }
                    var id = iterator.CurrentKey.Record;
                    if (!byId.TryGetValue(id, out var record) || record.Deleted)
                    {
                        throw TextErrors.Corrupt();
                    }
                    candidates.Add(record.Reference);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        postingObservation = null;
        var failures = new List<Exception>();
        if (entered)
        {
            ServerFailureObserver.Observe(() => slot.Exit(this), failures);
        }
        foreach (var failure in failures)
        {
            admission?.RetainFailure(failure);
        }
        try
        {
            admission?.Dispose();
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
