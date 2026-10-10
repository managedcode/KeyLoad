using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchCandidateWindow
{
    private const int FirstCandidate = 0;
    private const string MissingDocument = "The distributed search candidate is unavailable at its original cut.";

    internal static GlobalBranchWindow Capture(IKeyValueView view, SearchScore[] scores,
        PartitionRef partition, string collection, string branchName, GlobalBranchKind kind,
        string sourceWindowId, GlobalBranchScope scope, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        GlobalBranchOrder.SortScores(scores, budget);
        var admission = new GlobalBranchByteAdmission(budget.MaximumResultBytes, budget);
        admission.Accept(checked(PartitionQueryRetention.ArrayDescriptorBytes
            + (long)scores.Length * PartitionQueryRetention.PointerBytes));
        var candidates = ImmutableArray.CreateBuilder<GlobalBranchCandidate>(scores.Length);
        for (var index = FirstCandidate; index < scores.Length; index++)
        {
            budget.Check();
            var score = scores[index];
            var document = budget.ReadRecord<DocumentRecord>(view, DocumentStorageKeys.RecordKey(score.Reference));
            if (document is null || document.Deleted || document.Reference != score.Reference
                || document.Reference.Partition != partition || document.Reference.Collection != collection)
            { throw Errors.Fail(ErrorCode.HistoryUnavailable, MissingDocument); }
            var candidate = new GlobalBranchCandidate(document.Reference, document.Revision, score.Score);
            admission.Accept(NativeSerialization.Measure(candidate));
            candidates.Add(candidate);
        }
        var result = new GlobalBranchWindow(branchName, kind, sourceWindowId, scope,
            candidates.MoveToImmutable(), true, false, false, true);
        budget.CheckResult(result);
        return result;
    }
}
