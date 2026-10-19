using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class TextResourceProjectionLease : ITextProjectionLease, IOriginalTextPostingObservation
{
    private readonly ITextProjectionLease original;
    private readonly TextResourceReservation reservation;

    internal TextResourceProjectionLease(ITextProjectionLease original, TextResourceReservation reservation)
    {
        this.reservation = reservation;
        this.original = original;
    }

    internal TextResourceProjectionLease(Func<ITextProjectionLease> acquire, TextResourceReservation reservation)
    {
        this.reservation = reservation;
        original = acquire();
    }

    private bool disposed;
    public void ObserveOriginalPosting(Func<CancellationToken, ValueTask> callback)
    {
        if (original is IOriginalTextPostingObservation observed)
        {
            observed.ObserveOriginalPosting(callback);
        }
    }

    public void BeginRecord(EntityRef reference, long revision) => original.BeginRecord(reference, revision);
    public void ObserveToken(string token) => original.ObserveToken(token);
    public void VerifyCandidates(IReadOnlyList<string> terms, IReadOnlyList<EntityRef> references,
        ReadExecutionBudget budget) => original.VerifyCandidates(terms, references, budget);
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        original.Dispose();
        reservation.CompleteAfterJoinedCleanup();
        disposed = true;
    }
}
