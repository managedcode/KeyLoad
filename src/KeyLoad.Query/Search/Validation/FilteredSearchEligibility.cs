using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

/// <summary>Holds the caller's bounded, ordinal output allowlist for one search.</summary>
internal sealed class FilteredSearchEligibility
{
    private const int EmptyElementCount = 0;

    private readonly HashSet<string>? allowed;

    private FilteredSearchEligibility(HashSet<string>? allowed) => this.allowed = allowed;

    internal bool IsUnrestricted => allowed is null;
    internal bool IsEmpty => allowed is { Count: EmptyElementCount };
    internal int Count => allowed?.Count ?? int.MaxValue;

    internal static void ValidateRequest(ImmutableArray<string>? requested, DatabaseLimits limits,
        ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(budget);
        if (requested is null)
        {
            return;
        }
        var input = requested.Value;
        if (input.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Validation, FilteredSearchErrors.InvalidAllowlist);
        }
        if (input.Length > limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, FilteredSearchErrors.AllowlistExceeded);
        }
        foreach (var id in input)
        {
            budget.Check();
            JsonData.Identifier(id);
        }
    }

    internal static FilteredSearchEligibility Create(ImmutableArray<string>? requested, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (requested is null)
        {
            return new(null);
        }
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in requested.Value)
        {
            budget.Check();
            allowed.Add(id);
        }
        return new(allowed);
    }

    internal bool Allows(string id) => allowed is null || allowed.Contains(id);
}
