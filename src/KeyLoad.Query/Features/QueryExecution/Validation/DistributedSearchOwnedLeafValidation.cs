using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchOwnedLeafValidation
{
    private const int CurrentVersion = 1;
    private const int MinimumResultBytes = 1;
    private const int NoRecords = 0;
    private const long NoBytes = 0;
    private const string InvalidPhase = "The distributed search receiving phase is invalid.";
    private const string InvalidBudget = "The distributed search receiving phase exceeds its original grant.";
    private const string CanonicalTextRequired = "The distributed statistics require the canonical text profile.";

    internal static void Require(DistributedSearchOwnedLeafV1 request, DatabaseLimits limits,
        QueryExecutionOptions execution, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        if (request.Version != CurrentVersion || !Enum.IsDefined(request.Phase)
            || request.Search is null || request.Owner is null || string.IsNullOrEmpty(request.Tenant)
            || request.Search.Partition is null || request.Search.Partition.TenantId != request.Tenant
            || !ValidSlots(request))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
        if (request.MaxReadBytes < NoBytes || request.MaxReadBytes > limits.MaxQueryReadBytes
            || request.MaxExaminedRecords < NoRecords || request.MaxExaminedRecords > limits.MaxScanRecords
            || request.MaxResultBytes < MinimumResultBytes
            || request.MaxResultBytes > QueryResultBudgetPolicy.Resolve(limits, execution))
        { throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidBudget); }
        SearchRequestValidation.Validate(request.Search, limits, false, execution);
        FilteredSearchEligibility.ValidateRequest(request.Search.AllowedIds, limits, budget);
        if (request.Search.TextIndex is not null)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, CanonicalTextRequired); }
        if (request.Witness is { } witness && witness.Partition != request.Search.Partition)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
        budget.Check();
    }

    private static bool ValidSlots(DistributedSearchOwnedLeafV1 request)
        => request.Phase switch
        {
            DistributedSearchPhase.Statistics => request.Witness is null && request.Statistics is null
                && request.Scope is null && request.SourceWindowId is null && request.Selected.IsDefault,
            DistributedSearchPhase.Candidates => request.Witness is not null && request.Statistics is not null
                && request.Scope is not null && request.SourceWindowId is not null && request.Selected.IsDefault,
            DistributedSearchPhase.Projection => request.Witness is not null && request.Statistics is null
                && request.Scope is null && request.SourceWindowId is null && !request.Selected.IsDefault,
            DistributedSearchPhase.Revalidate => request.Witness is not null && request.Statistics is null
                && request.Scope is null && request.SourceWindowId is null && request.Selected.IsDefault,
            _ => false
        };
}
