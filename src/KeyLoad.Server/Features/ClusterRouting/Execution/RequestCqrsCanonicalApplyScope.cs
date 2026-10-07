using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsCanonicalApplyScope : IDisposable
{
    private readonly RequestCqrsCanonicalApplyObserver owner;
    private readonly RequestCqrsProbeClaim claim;
    private readonly Action restore;
    private readonly IDisposable network;
    private readonly Action<RequestCqrsCanonicalApplyScope, bool> holding;
    private const int Open = 0;
    private const int Closed = 1;
    private int closed;
    private int appendObserved;
    private int outboundObserved;
    internal RequestCqrsCanonicalApplyScope(RequestCqrsCanonicalApplyObserver owner, RequestCqrsProbeClaim claim, Action restore,
        Action<RequestCqrsCanonicalApplyScope, bool> holding)
    {
        this.owner = owner;
        this.claim = claim;
        this.restore = restore;
        this.holding = holding;
        network = ReplicaCanonicalApplyObservation.Enter(ObserveOutbound);
    }
    internal void JournalFlushed()
    {
        holding(this, true);
        try
        { owner.HoldCanonical(claim); }
        finally { holding(this, false); }
    }
    internal bool Matches(AppendRequest request)
        => request.Entries.IsEmpty && request.PreviousIndex >= claim.EntryIndex!.Value
            && request.CommittedIndex >= claim.EntryIndex.Value && request.Term >= claim.EntryTerm!.Value;
    internal void IndependentAppendCompleted()
    {
        if (Interlocked.Exchange(ref appendObserved, Closed) == Open)
        { owner.ObserveIndependentAppend(claim); }
    }
    private void ObserveOutbound()
    {
        if (Interlocked.Exchange(ref outboundObserved, Closed) == Open)
        { owner.ObserveCanonicalOutbound(claim); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref closed, Closed) != Open)
        { return; }
        try
        {
            network.Dispose();
        }
        catch (Exception primary)
        {
            try
            {
                CompleteDisposal();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(primary, cleanup);
            }
            throw;
        }
        CompleteDisposal();
    }

    private void CompleteDisposal()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(restore, failures);
        ServerFailureObserver.Observe(() => owner.ExitCanonical(claim), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
