using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextSelectedProjectionLease : ITextProjectionLease, IOriginalTextPostingObservation
{
    private readonly INativeTextSelectedIndexLeaseOwner slot;
    private readonly NativeTextSelectedReadLease admission;
    private readonly ReadExecutionBudget budget;
    private readonly IOptions<DatabaseLimits> limits;
    private readonly HashSet<EntityRef> visited = [];
    private readonly Dictionary<EntityRef, NativeTextIncrementalRecord> byReference;
    private readonly Dictionary<ulong, NativeTextIncrementalRecord> byId;
    private readonly bool entered;
    private bool completed;
    private bool disposed;
    private Func<CancellationToken, ValueTask>? postingObservation;
    private bool postingObserved;

    internal NativeTextSelectedProjectionLease(NativeTextSelectedReadAdmission admissionOwner,
        Func<(INativeTextSelectedIndexLeaseOwner Slot, NativeTextIncrementalManifest Manifest)> capture,
        ReadExecutionBudget budget, IOptions<DatabaseLimits> limits)
    {
        this.budget = budget;
        this.limits = limits;
        try
        {
            admission = admissionOwner.EnterRead();
            var selected = capture();
            slot = selected.Slot;
            slot.Enter(budget, this);
            entered = true;
            byReference = NativeTextSelectedRecordMap.ByReference(selected.Manifest, budget);
            byId = NativeTextSelectedRecordMap.ById(selected.Manifest, budget);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            { Dispose(); }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public void ObserveOriginalPosting(Func<CancellationToken, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (disposed || completed || postingObservation is not null)
        { throw NativeTextErrors.Corrupt(); }
        postingObservation = callback;
    }

    private void ObserveOriginalPosting()
    {
        var callback = postingObservation;
        if (callback is null || postingObserved)
        { return; }
        postingObserved = true;
        var failures = new List<Exception>();
        ServerFailureObserver.ObserveAsync(() => callback(budget.Cancellation).AsTask(), failures).GetAwaiter().GetResult();
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void BeginRecord(EntityRef reference, long revision)
    {
        budget.Check();
        budget.ChargeBytes(NativeSerialization.Measure(reference));
        if (disposed || completed || !visited.Add(reference))
        { throw NativeTextErrors.Corrupt(); }
        if (!byReference.TryGetValue(reference, out var record) || record.Deleted || record.Revision != revision)
        { throw NativeTextErrors.Mismatch(); }
    }

    public void ObserveToken(string token)
    {
        budget.Check();
        if (disposed || completed || string.IsNullOrEmpty(token))
        { throw NativeTextErrors.Corrupt(); }
    }

    public void VerifyCandidates(IReadOnlyList<string> terms, IReadOnlyList<EntityRef> references,
        ReadExecutionBudget verificationBudget)
    {
        if (disposed || completed || verificationBudget != budget)
        { throw NativeTextErrors.Corrupt(); }
        if (references.Count > limits.Value.MaxScanRecords || terms.Count > limits.Value.MaxSearchTextTokens)
        { throw NativeTextErrors.BoundExceeded(); }
        var candidates = new HashSet<EntityRef>();
        foreach (var term in terms)
        {
            budget.ChargeBytes(System.Text.Encoding.UTF8.GetByteCount(term));
            ReadCandidates(NativeTextHash.Sha256(term), candidates);
        }
        foreach (var reference in references)
        {
            budget.Check();
            if (!visited.Contains(reference) || !candidates.Contains(reference))
            { throw NativeTextErrors.Corrupt(); }
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
                iterator.Seek(NativeTextIndex.LowerBound(token));
                while (true)
                {
                    budget.ChargeBytes(NativeTextProtocol.PostingBytes);
                    if (!iterator.Next())
                    { return; }
                    slot.ObserveOriginalPostingRead();
                    ObserveOriginalPosting();
                    budget.Check();
                    budget.Cancellation.ThrowIfCancellationRequested();
                    if (iterator.CurrentKey.Token != token)
                    { return; }
                    var id = iterator.CurrentKey.Record;
                    if (!byId.TryGetValue(id, out var record) || record.Deleted)
                    { throw NativeTextErrors.Corrupt(); }
                    budget.ChargeBytes(NativeSerialization.Measure(record.Reference));
                    candidates.Add(record.Reference);
                    if (candidates.Count > limits.Value.MaxScanRecords)
                    { throw NativeTextErrors.BoundExceeded(); }
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        postingObservation = null;
        var failures = new List<Exception>();
        if (entered)
        { ServerFailureObserver.Observe(() => slot.Exit(this), failures); }
        foreach (var failure in failures)
        { admission?.RetainFailure(failure); }
        try
        { admission?.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
