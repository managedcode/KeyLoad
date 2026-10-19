using System.Collections.Immutable;
using ZoneTree.Analytics;

namespace KeyLoad.Core.Features.AnalyticsStore;

internal static class AnalyticsStoreResult
{
    private const int EqualDimensionOrder = 0;
    private const string Invalid = "The execution analytics result does not cover the original canonical input.";
    internal static AnalyticsAggregateResult Create(AnalyticsAggregateSnapshot snapshot, AnalyticsStoreDimensions captured, AnalyticsResult actual)
    {
        if (actual.Totals[Metric.Events] != snapshot.Rows.Length || actual.Rows.Count > snapshot.Request.MaxGroups
            || !actual.Plan.IsComplete || actual.TotalRows != actual.Rows.Count)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, Invalid);
        }

        var rows = new List<AnalyticsAggregateRow>();
        if (!snapshot.Rows.IsEmpty)
        {
            foreach (var row in actual.Rows)
            {
                rows.Add(CreateRow(snapshot, captured, row));
            }
        }
        rows.Sort((left, right) => Compare(snapshot.Request.GroupBy, left, right));
        var result = new AnalyticsAggregateResult([.. rows], snapshot.Rows.Length);
        snapshot.Work.CheckResult(result);
        snapshot.Work.Check();
        return result;
    }

    private static AnalyticsAggregateRow CreateRow(AnalyticsAggregateSnapshot snapshot,
        AnalyticsStoreDimensions captured, AnalyticsRow row)
    {
        if (row.Bucket is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }

        var dimensions = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var metrics = ImmutableDictionary.CreateBuilder<string, double>(StringComparer.Ordinal);
        foreach (var name in snapshot.Request.GroupBy)
        {
            dimensions.Add(name, captured.FromResult(name, row[AnalyticsStoreMapping.Dimension(snapshot.Schema, name)]));
        }

        foreach (var selection in snapshot.Request.Metrics)
        {
            var value = row[AnalyticsStoreMapping.Metric(snapshot, selection)];
            if (!double.IsFinite(value))
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, Invalid);
            }

            metrics.Add(selection.Name, value);
        }
        return new(dimensions.ToImmutable(), metrics.ToImmutable());
    }

    private static int Compare(ImmutableArray<string> groupBy, AnalyticsAggregateRow left, AnalyticsAggregateRow right)
    {
        foreach (var name in groupBy)
        {
            var compared = StringComparer.Ordinal.Compare(left.Dimensions[name], right.Dimensions[name]);
            if (compared != EqualDimensionOrder)
            {
                return compared;
            }
        }
        return EqualDimensionOrder;
    }
}
