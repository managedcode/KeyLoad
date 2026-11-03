using KeyLoad.Core;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal sealed class TextSharedProjectionLease(ITextProjectionLease original,
    TextSelectedReadLease admission) : ITextProjectionLease, ITextCertifiedProjectionLease
{
    private bool disposed;
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
        disposed = true;
        try
        {
            original.Dispose();
        }
        catch (Exception primary)
        {
            admission.RetainFailure(primary);
            try
            {
                admission.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        admission.Dispose();
    }
}
