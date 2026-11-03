using KeyLoad.Core;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal sealed class TextProjectionLease(RuntimeTextProjection owner, TextGeneration generation,
    TextGenerationSlot slot, ReadExecutionBudget budget, Func<string, ulong> tokenHash,
    Action<TextFaultStage>? faultObserver,
    bool building, IOptions<TextExecutionOptions> executionOptions) : ITextProjectionLease, ITextCertifiedProjectionLease
{
    private const int RecordIndexSingleItemCount = -1;

    private int recordIndex = RecordIndexSingleItemCount;
    private ulong previousToken;
    private int postingsSinceBoundCheck;
    private bool recordOpen;
    private bool completed;
    private bool certifiedRead;
    private bool invalid;
    private bool disposed;
    private bool postingObserved;

    public void BeginRecord(EntityRef reference, long revision)
    {
        const int PreviousTokenEmptyCount = 0;

        budget.Check();
        if (disposed || completed)
        {
            throw TextErrors.Corrupt();
        }
        recordIndex++;
        if (building)
        {
            AddBuildingRecord(reference, revision);
        }
        else if (recordIndex >= generation.Corpus.Records.Count
            || generation.Corpus.Records[recordIndex].Reference != reference
            || generation.Corpus.Records[recordIndex].Revision != revision)
        {
            invalid = true;
            throw TextErrors.Mismatch();
        }
        previousToken = PreviousTokenEmptyCount;
        recordOpen = true;
    }

    public void ObserveToken(ReadOnlySpan<char> token)
    {
        const int PostingsSinceBoundCheckEmptyCount = 0;

        budget.Check();
        if (disposed || completed || !recordOpen || token.IsEmpty)
        {
            throw TextErrors.Corrupt();
        }
        if (!building)
        {
            return;
        }
        var hash = tokenHash(token.ToString());
        WriteBuildingPosting(hash);
        if (!postingObserved)
        {
            postingObserved = true;
            faultObserver?.Invoke(TextFaultStage.PostingWritten);
        }
        previousToken = hash;
        if (++postingsSinceBoundCheck >= executionOptions.Value.PostingsBoundCheckInterval)
        {
            owner.CheckPhysical(budget);
            postingsSinceBoundCheck = PostingsSinceBoundCheckEmptyCount;
        }
    }

    public void VerifyCandidates(IReadOnlyList<string> terms, IReadOnlyList<EntityRef> references,
        ReadExecutionBudget verificationBudget)
    {
        ArgumentNullException.ThrowIfNull(terms) /* KLD0041: BCL null validation accepts the terms IReadOnlyList reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(references) /* KLD0041: BCL null validation accepts the references IReadOnlyList reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(verificationBudget) /* KLD0041: BCL null validation accepts the verificationBudget ReadExecutionBudget reference through its object parameter. */;
        if (disposed || completed || verificationBudget != budget)
        {
            throw TextErrors.Corrupt();
        }
        try
        {
            VerifyCompleteVisit();
            TextCandidateReader.Verify(generation, terms, references, budget, tokenHash,
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

    public TextPostingCandidates? ReadCertifiedCandidates(IReadOnlyList<string> terms, ReadExecutionBudget verificationBudget)
    {
        if (disposed || completed || certifiedRead || recordOpen || verificationBudget != budget)
        {
            throw TextErrors.Corrupt();
        }
        if (building)
        {
            return null;
        }
        try
        {
            certifiedRead = true;
            return TextCandidateReader.ReadCertified(generation, terms, budget, tokenHash);
        }
        catch (Exception error) when (error is not OperationCanceledException
            && error is not KeyLoadException { Code: KeyLoad.ErrorCode.BudgetExceeded })
        {
            invalid = true;
            throw;
        }
    }

    public void SettleCertifiedCandidates(bool valid)
    {
        if (disposed || !certifiedRead || completed || invalid)
        {
            throw TextErrors.Corrupt();
        }
        completed = valid;
        invalid = !valid;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        owner.Release(generation, slot, budget, building, invalid, completed);
    }

    private void AddBuildingRecord(EntityRef reference, long revision)
        => owner.MutatePhysical(() => generation.Corpus.AddRecord(reference, revision));

    private void WriteBuildingPosting(ulong hash)
    {
        const int RecordIndexStep = 1;
        var ordinal = (ulong)recordIndex + RecordIndexStep;
        owner.MutatePhysical(() =>
        {
            generation.Index.UpsertRecord(hash, ordinal, previousToken);
            generation.Corpus.ObservePosting(hash, ordinal, previousToken);
        });
    }

    private void VerifyCompleteVisit()
    {
        const int RecordIndexStep = 1;

        if (building)
        {
            owner.CheckPhysical(budget);
            return;
        }
        if (recordIndex + RecordIndexStep != generation.Corpus.Records.Count)
        {
            invalid = true;
            throw TextErrors.Mismatch();
        }
    }
}
