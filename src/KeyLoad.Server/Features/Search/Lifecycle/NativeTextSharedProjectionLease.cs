using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextSharedProjectionLease(ITextProjectionLease original,
    NativeTextSelectedReadLease admission) : ITextProjectionLease
{
    private bool disposed;
    public void BeginRecord(EntityRef reference, long revision) => original.BeginRecord(reference, revision);
    public void ObserveToken(string token) => original.ObserveToken(token);
    public void VerifyCandidates(IReadOnlyList<string> terms, IReadOnlyList<EntityRef> references,
        ReadExecutionBudget budget) => original.VerifyCandidates(terms, references, budget);
    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        try
        { original.Dispose(); }
        catch (Exception primary)
        {
            admission.RetainFailure(primary);
            try
            { admission.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        admission.Dispose();
    }
}
