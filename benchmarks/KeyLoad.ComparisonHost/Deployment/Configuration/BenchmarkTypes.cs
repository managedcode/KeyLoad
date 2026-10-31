namespace KeyLoad.ComparisonHost.Deployment;

/// <summary>The data types with their own standard dataset and scenario; the job's <c>Benchmarks__Type</c> is one of these.</summary>
internal static class BenchmarkTypes
{
    internal const string Documents = "documents";
    internal const string KeyValue = "key-value";
    internal const string Relational = "relational";
    internal const string Graph = "graph";
    internal const string Vector = "vector";
    internal const string Search = "search";
    internal const string TimeSeries = "time-series";
    internal const string Analytics = "analytics";
    internal const string Queue = "queue";
    internal const string Events = "events";
    internal const string NoDriver = "No driver for the type ";
    internal const string ForDatabase = " exists for the database ";
}
