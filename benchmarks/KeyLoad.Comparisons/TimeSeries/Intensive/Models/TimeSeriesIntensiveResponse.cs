using System.Collections.Immutable;

namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries.Intensive;

internal readonly record struct TimeSeriesIntensiveResponse(ImmutableArray<SampleRecord> Raw = default,
    SampleRecord? Latest = null, SampleAggregate? Aggregate = null,
    ImmutableArray<SampleAggregateWindow> Windows = default, TimeSeriesIntensiveAppendReceipt? Receipt = null)
{
    private const int SingleItemCount = 1;
    private const int NoObservedItems = 0;

    internal global::ManagedCode.Communication.Result<long?> Count(TimeSeriesIntensiveScenario scenario) => scenario switch
    {
        TimeSeriesIntensiveScenario.Append => Receipt is null ? global::ManagedCode.Communication.Result<long?>.FailNotFound(nameof(Count)) : global::ManagedCode.Communication.Result<long?>.Succeed(((long?)(SingleItemCount))),
        TimeSeriesIntensiveScenario.RawRangeRead => Raw.IsDefault ? global::ManagedCode.Communication.Result<long?>.FailNotFound(nameof(Count)) : global::ManagedCode.Communication.Result<long?>.Succeed(((long?)(Raw.Length))),
        TimeSeriesIntensiveScenario.Latest => Latest is null ? global::ManagedCode.Communication.Result<long?>.Succeed(((long?)(NoObservedItems))) : global::ManagedCode.Communication.Result<long?>.Succeed(((long?)(SingleItemCount))),
        TimeSeriesIntensiveScenario.Aggregate => ((long?)(Aggregate?.Count)) is { } outcomeValue7 ? global::ManagedCode.Communication.Result<long?>.Succeed(outcomeValue7) : global::ManagedCode.Communication.Result<long?>.FailNotFound(nameof(Count)),
        TimeSeriesIntensiveScenario.Windows => Windows.IsDefault ? global::ManagedCode.Communication.Result<long?>.FailNotFound(nameof(Count)) : global::ManagedCode.Communication.Result<long?>.Succeed(((long?)(Windows.Length))),
        _ => global::ManagedCode.Communication.Result<long?>.FailNotFound(nameof(Count))
    };
}
