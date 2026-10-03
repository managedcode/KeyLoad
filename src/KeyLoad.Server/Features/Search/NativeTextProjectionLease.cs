using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjectionLease(NativeTextProjection owner, NativeTextGeneration generation,
    ReadExecutionBudget budget, Func<string, ulong> tokenHash, Action<NativeTextFaultStage>? faultObserver,
    bool building) : ITextProjectionLease
{
    private int recordIndex = -1;
    private ulong previousToken;
    private int postingsSinceBoundCheck;
    private bool recordOpen;
    private bool completed;
    private bool invalid;
    private bool disposed;
    private bool postingObserved;

    public void BeginRecord(EntityRef reference, long revision)
    {
        budget.Check();
        if (disposed || completed)
        {
            throw NativeTextErrors.Corrupt();
        }
        recordIndex++;
        if (building)
        {
            generation.AddRecord(reference, revision, budget);
        }
        else if (recordIndex >= generation.Records.Count
            || generation.Records[recordIndex].Reference != reference
            || generation.Records[recordIndex].Revision != revision)
        {
            invalid = true;
            throw NativeTextErrors.Mismatch();
        }
        previousToken = 0;
        recordOpen = true;
    }

    public void ObserveToken(string token)
    {
        budget.Check();
        if (disposed || completed || !recordOpen || string.IsNullOrEmpty(token))
        {
            throw NativeTextErrors.Corrupt();
        }
        if (!building)
        {
            return;
        }
        var hash = tokenHash(token);
        generation.Index.UpsertRecord(hash, (ulong)recordIndex + 1, previousToken);
        if (!postingObserved)
        {
            postingObserved = true;
            faultObserver?.Invoke(NativeTextFaultStage.NativePostingWritten);
        }
        previousToken = hash;
        if (++postingsSinceBoundCheck >= 64)
        {
            NativeTextFiles.CheckGenerationBound(generation.Path, budget);
            postingsSinceBoundCheck = 0;
        }
    }

    public void VerifyCandidates(IReadOnlyList<string> terms, IReadOnlyList<EntityRef> references,
        ReadExecutionBudget verificationBudget)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(verificationBudget);
        if (disposed || completed || verificationBudget != budget)
        {
            throw NativeTextErrors.Corrupt();
        }
        try
        {
            VerifyCompleteVisit();
            NativeTextCandidateReader.Verify(generation, terms, references, budget, tokenHash,
                () => invalid = true);
            if (building)
            {
                owner.Publish(generation, budget);
            }
            completed = true;
        }
        catch (Exception error)
        {
            if (!building && error is not OperationCanceledException
                && error is not KeyLoadException { Code: KeyLoad.ErrorCode.BudgetExceeded })
            {
                invalid = true;
            }
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        owner.Release(generation, budget, building, invalid, completed);
    }

    private void VerifyCompleteVisit()
    {
        if (building)
        {
            NativeTextFiles.CheckGenerationBound(generation.Path);
            return;
        }
        if (recordIndex + 1 != generation.Records.Count)
        {
            invalid = true;
            throw NativeTextErrors.Mismatch();
        }
    }
}
