namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal sealed record TimeSeriesTargetMetadata(string Name, string StorageModel, string PersistenceGuarantee,
    string AcknowledgementGuarantee, string? Image, string? PackageVersion);
