using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchReplyShapeValidation
{
    private const int FirstCandidate = 0;
    private const string InvalidReply = "The distributed search reply contains a foreign or incomplete phase payload.";

    internal static void Require(DistributedSearchOwnedLeafV1 request,
        DistributedSearchLeafResultV1 result, ReadExecutionBudget budget)
    {
        if (result.Candidates is { } candidates)
        {
            RequireWindow(request, candidates.Text, request.Search.Text is not null,
                DistributedSearchBranchNames.Text, GlobalBranchKind.Text, budget);
            RequireWindow(request, candidates.Vector, request.Search.Vector is not null,
                DistributedSearchBranchNames.Vector, GlobalBranchKind.Vector, budget);
        }
        if (result.Projection is { } projection)
        {
            if (projection.Hits.IsDefault || projection.Hits.Length != request.Selected.Length)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidReply); }
            for (var index = FirstCandidate; index < request.Selected.Length; index++)
            {
                budget.Check();
                var expected = request.Selected[index];
                var hit = projection.Hits[index];
                if (hit is null || hit.Document is null || hit.Document.Reference != expected.Reference
                    || hit.Document.Revision != expected.Revision || hit.Explanation is not null
                    || BitConverter.DoubleToInt64Bits(hit.Score) != BitConverter.DoubleToInt64Bits(expected.Score))
                { throw Errors.Fail(ErrorCode.Corruption, InvalidReply); }
            }
        }
    }

    private static void RequireWindow(DistributedSearchOwnedLeafV1 request,
        GlobalBranchWindow? window, bool expected, string name, GlobalBranchKind kind,
        ReadExecutionBudget budget)
    {
        if (!expected)
        {
            if (window is not null)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidReply); }
            return;
        }
        if (window is null || window.Kind != kind || window.BranchName != name
            || window.SourceWindowId != request.SourceWindowId || window.Scope != request.Scope
            || window.Candidates.IsDefault || !window.Complete || !window.Exhaustive
            || window.Approximate || window.Truncated)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidReply); }
        foreach (var candidate in window.Candidates)
        {
            budget.Check();
            GlobalBranchValidation.ValidateCandidate(candidate);
            if (candidate.Reference.Partition != request.Search.Partition
                || candidate.Reference.Collection != request.Search.Collection)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidReply); }
        }
    }
}
