using KeyLoad.Core;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal sealed class TextResourceProjectionLease : ITextProjectionLease, IOriginalTextPostingObservation, ITextCertifiedProjectionLease
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
    public void ObserveToken(ReadOnlySpan<char> token) => original.ObserveToken(token);
    public void VerifyCandidates(IReadOnlyList<string> terms, IReadOnlyList<EntityRef> references,
        ReadExecutionBudget budget) => original.VerifyCandidates(terms, references, budget);
    public TextPostingCandidates? ReadCertifiedCandidates(IReadOnlyList<string> terms, ReadExecutionBudget budget)
        => (original as ITextCertifiedProjectionLease)?.ReadCertifiedCandidates(terms, budget);
    public void SettleCertifiedCandidates(bool valid)
        => (original as ITextCertifiedProjectionLease)?.SettleCertifiedCandidates(valid);
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
