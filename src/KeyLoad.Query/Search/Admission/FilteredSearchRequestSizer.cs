using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

/// <summary>Measures canonical request bytes without retaining an unbounded serialized copy.</summary>
internal static class FilteredSearchRequestSizer
{
    internal static void EnsureBounded<TRequest>(TRequest request, int maximumBytes, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        using var sink = new FilteredSearchCountingStream(maximumBytes);
        using var writer = new Utf8JsonWriter(sink);
        JsonSerializer.Serialize(writer, request, JsonDefaults.Options);
        writer.Flush();
        budget.Check();
    }
}
