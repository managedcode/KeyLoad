namespace KeyLoad;

/// <summary>Supported complete finite analytics aggregation operations.</summary>
[Orleans.Alias(AnalyticsStoreContractAliases.AggregateKind)]
public enum AnalyticsAggregateKind
{
    /// <summary>Counts authorized canonical events; its metric name must be empty.</summary>
    Count = 0,
    /// <summary>Sums finite metric values.</summary>
    Sum = 1,
    /// <summary>Averages finite metric values.</summary>
    Average = 2,
    /// <summary>Returns the minimum metric value.</summary>
    Min = 3,
    /// <summary>Returns the maximum metric value.</summary>
    Max = 4
}
