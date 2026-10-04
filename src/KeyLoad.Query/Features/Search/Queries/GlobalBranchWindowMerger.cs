using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal static class GlobalBranchWindowMerger
{
    private const string ResourceExceeded = "The global branch merge exceeds its configured bounds.";
    private const string ConflictingDuplicate = "A global branch candidate has conflicting revision or score evidence.";
    private const string InvalidOrder = "A global branch window is not in canonical score order.";
    private const string MissingWindow = "A required global branch window is unavailable or incomplete.";
    private const string ExactUnavailable = "The requested exact branch profile is unavailable.";

    internal static GlobalBranchMergeResult Merge(GlobalBranchMergeRequest request,
        ImmutableArray<GlobalBranchWindow> windows, DatabaseLimits limits, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        var bytes = new GlobalBranchByteAdmission(limits.MaxBatchBytes, budget);
        var received = GlobalBranchValidation.ValidateRequest(request, windows, limits, budget, bytes);
        budget.Check();
        var candidateCount = PreflightWindows(windows, limits.MaxScanRecords, budget);
        var missing = MissingIds(request.ExpectedWindowIds, received, budget);
        var complete = missing.IsEmpty;
        var approximate = false;
        var truncated = false;
        var allExhaustive = true;
        foreach (var window in windows)
        {
            budget.Check();
            complete &= window.Complete;
            approximate |= window.Approximate;
            truncated |= window.Truncated;
            allExhaustive &= window.Exhaustive;
        }
        RequireStrictCompleteness(request, missing, complete, approximate, truncated);
        PreflightCandidates(windows, candidateCount, budget, bytes);
        var candidates = Deduplicate(windows, candidateCount, budget);
        var ordered = candidates.Values.ToArray();
        GlobalBranchOrder.SortCandidates(ordered, budget);
        truncated |= ordered.Length > request.Limit;
        var exhaustive = complete && !approximate && !truncated && candidates.Count <= request.Limit
            && allExhaustive;
        var selectedCount = Math.Min(ordered.Length, request.Limit);
        var selectedBuilder = ImmutableArray.CreateBuilder<GlobalBranchCandidate>(selectedCount);
        for (var index = 0; index < selectedCount; index++)
        {
            budget.Check();
            selectedBuilder.Add(ordered[index]);
        }
        var selected = selectedBuilder.MoveToImmutable();
        budget.Check();
        return new(selected, missing, complete, approximate, truncated, exhaustive, candidateCount);
    }

    private static int PreflightWindows(ImmutableArray<GlobalBranchWindow> windows, int maximumRecords,
        ReadExecutionBudget budget)
    {
        var records = 0;
        foreach (var window in windows)
        {
            budget.Check();
            if (window.Candidates.Length > maximumRecords - records)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
            }
            records += window.Candidates.Length;
        }
        return records;
    }

    private static void PreflightCandidates(ImmutableArray<GlobalBranchWindow> windows, int expectedCount,
        ReadExecutionBudget budget, GlobalBranchByteAdmission bytes)
    {
        var examined = 0;
        foreach (var window in windows)
        {
            GlobalBranchCandidate? previous = null;
            foreach (var candidate in window.Candidates)
            {
                budget.Check();
                GlobalBranchValidation.ValidateCandidateEncodingBounds(candidate);
                bytes.Accept(NativeSerialization.Measure(candidate));
                GlobalBranchValidation.ValidateCandidate(candidate);
                if (previous is not null && GlobalBranchOrder.CompareCandidates(previous, candidate) > 0)
                {
                    throw Errors.Fail(ErrorCode.Corruption, InvalidOrder);
                }
                previous = candidate;
                examined++;
            }
        }
        if (examined != expectedCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidOrder);
        }
    }

    private static ImmutableArray<string> MissingIds(ImmutableArray<string> expected,
        HashSet<string> received, ReadExecutionBudget budget)
    {
        var missingCount = expected.Length - received.Count;
        var missing = ImmutableArray.CreateBuilder<string>(missingCount);
        foreach (var id in expected)
        {
            budget.Check();
            if (!received.Contains(id))
            {
                missing.Add(id);
            }
        }
        return missing.MoveToImmutable();
    }

    private static void RequireStrictCompleteness(GlobalBranchMergeRequest request,
        ImmutableArray<string> missing, bool complete, bool approximate, bool truncated)
    {
        if (!request.AllowIncomplete && (missing.Length > 0 || !complete))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, MissingWindow);
        }
        if (!request.AllowIncomplete && (approximate || truncated))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, ExactUnavailable);
        }
    }

    private static Dictionary<EntityRef, GlobalBranchCandidate> Deduplicate(
        ImmutableArray<GlobalBranchWindow> windows, int candidateCount, ReadExecutionBudget budget)
    {
        var candidates = new Dictionary<EntityRef, GlobalBranchCandidate>(candidateCount);
        foreach (var window in windows)
        {
            foreach (var candidate in window.Candidates)
            {
                budget.Check();
                AddCandidate(candidates, candidate);
            }
        }
        return candidates;
    }

    private static void AddCandidate(Dictionary<EntityRef, GlobalBranchCandidate> candidates,
        GlobalBranchCandidate candidate)
    {
        if (!candidates.TryGetValue(candidate.Reference, out var existing))
        {
            candidates.Add(candidate.Reference, candidate);
            return;
        }
        if (existing.Revision != candidate.Revision
            || BitConverter.DoubleToInt64Bits(existing.Score) != BitConverter.DoubleToInt64Bits(candidate.Score))
        {
            throw Errors.Fail(ErrorCode.Corruption, ConflictingDuplicate);
        }
    }
}
