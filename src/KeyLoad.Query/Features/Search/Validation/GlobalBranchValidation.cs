using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal static class GlobalBranchValidation
{
    private const int MinimumPositiveCount = 1;

    private const string InvalidRequest = "The global branch merge request is invalid.";
    private const string InvalidWindow = "A global branch window is malformed or incomparable.";
    private const string ResourceExceeded = "The global branch merge exceeds its configured bounds.";
    private const int MaximumIdentifierCharacters = 256;

    internal static HashSet<string> ValidateRequest(GlobalBranchMergeRequest request, ImmutableArray<GlobalBranchWindow> windows,
        DatabaseLimits limits, ReadExecutionBudget budget, GlobalBranchByteAdmission bytes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(budget);
        if (!Enum.IsDefined(request.Kind) || request.Scope is null || request.ExpectedWindowIds.IsDefault
            || windows.IsDefault || request.Limit < MinimumPositiveCount || request.Limit > limits.MaxResults
            || request.Limit > limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        if (request.ExpectedWindowIds.Length > limits.MaxScanRecords || windows.Length > limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
        PreflightIdentifiers(request, windows, budget);
        bytes.Accept(NativeSerialization.Measure(request));
        PreflightWindowHeaders(windows, budget, bytes);
        var expected = ValidateExpectedIds(request.ExpectedWindowIds, budget);
        return ValidateWindowHeaders(request, windows, expected, budget);
    }

    private static void PreflightIdentifiers(GlobalBranchMergeRequest request,
        ImmutableArray<GlobalBranchWindow> windows, ReadExecutionBudget budget)
    {
        CheckIdentifier(request.BranchName);
        ValidateRequestScope(request.Scope);
        foreach (var id in request.ExpectedWindowIds)
        {
            budget.Check();
            CheckIdentifier(id);
        }
        foreach (var window in windows)
        {
            budget.Check();
            if (window is null || window.Scope is null || window.Candidates.IsDefault)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidWindow);
            }
            CheckIdentifier(window.BranchName);
            CheckIdentifier(window.SourceWindowId);
            ValidateScope(window.Scope);
        }
    }

    private static void PreflightWindowHeaders(ImmutableArray<GlobalBranchWindow> windows,
        ReadExecutionBudget budget, GlobalBranchByteAdmission bytes)
    {
        foreach (var window in windows)
        {
            budget.Check();
            if (window is null || window.Scope is null || window.Candidates.IsDefault)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidWindow);
            }
            bytes.Accept(NativeSerialization.Measure(window with { Candidates = [] }));
        }
    }

    internal static void ValidateCandidate(GlobalBranchCandidate candidate)
    {
        ValidateCandidateEncodingBounds(candidate);
        if (candidate.Revision < MinimumPositiveCount || !double.IsFinite(candidate.Score))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidWindow);
        }
        ValidateReference(candidate.Reference);
    }

    internal static void ValidateCandidateEncodingBounds(GlobalBranchCandidate candidate)
    {
        if (candidate is null || candidate.Reference is null || candidate.Reference.Partition is null
            || IsUnbounded(candidate.Reference.Partition.TenantId)
            || IsUnbounded(candidate.Reference.Partition.DatabaseId)
            || IsUnbounded(candidate.Reference.Partition.TransactionDomainId)
            || IsUnbounded(candidate.Reference.Partition.PartitionKey)
            || IsUnbounded(candidate.Reference.Collection) || IsUnbounded(candidate.Reference.Id))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidWindow);
        }
    }

    internal static void ValidateIdentifier(string value)
    {
        try
        {
            CheckIdentifier(value);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidWindow);
        }
    }

    private static HashSet<string> ValidateExpectedIds(ImmutableArray<string> ids, ReadExecutionBudget budget)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            budget.Check();
            if (!unique.Add(id))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
            }
        }
        return unique;
    }

    private static HashSet<string> ValidateWindowHeaders(GlobalBranchMergeRequest request,
        ImmutableArray<GlobalBranchWindow> windows, HashSet<string> expected, ReadExecutionBudget budget)
    {
        var received = new HashSet<string>(StringComparer.Ordinal);
        foreach (var window in windows)
        {
            budget.Check();
            if (!Enum.IsDefined(window.Kind) || !string.Equals(window.BranchName, request.BranchName, StringComparison.Ordinal)
                || window.Kind != request.Kind || window.Scope != request.Scope)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidWindow);
            }
            if (!expected.Contains(window.SourceWindowId) || !received.Add(window.SourceWindowId))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
            }
        }
        return received;
    }

    private static void ValidateRequestScope(GlobalBranchScope scope)
    {
        CheckIdentifier(scope.ScoreProfile);
        CheckIdentifier(scope.CorpusScope);
        CheckIdentifier(scope.StatisticsEpoch);
    }

    private static void ValidateScope(GlobalBranchScope scope)
    {
        ValidateIdentifier(scope.ScoreProfile);
        ValidateIdentifier(scope.CorpusScope);
        ValidateIdentifier(scope.StatisticsEpoch);
    }

    private static void ValidateReference(EntityRef reference)
    {
        ValidateIdentifier(reference.Partition.TenantId);
        ValidateIdentifier(reference.Partition.DatabaseId);
        ValidateIdentifier(reference.Partition.TransactionDomainId);
        ValidateIdentifier(reference.Partition.PartitionKey);
        ValidateIdentifier(reference.Collection);
        ValidateIdentifier(reference.Id);
    }

    private static void CheckIdentifier(string value)
    {
        if (value is null || value.Length > MaximumIdentifierCharacters)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        JsonData.Identifier(value);
    }

    private static bool IsUnbounded(string? value)
        => value is null || value.Length > MaximumIdentifierCharacters;
}
