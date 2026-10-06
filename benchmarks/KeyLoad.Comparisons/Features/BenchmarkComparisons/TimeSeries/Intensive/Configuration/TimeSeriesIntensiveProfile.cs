namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveProfile
{
    internal const string Name = "intensive-timeseries-4096-c16";
    internal const string SeedSeries = "seed";
    internal const string Tags = "{\"kind\":\"intensive\",\"revision\":1}";
    internal const int RandomSeed = 1729;
    internal const int SampleCount = 4096;
    internal const int OperationCount = 10000;
    internal const int WarmupCount = 256;
    internal const int RepetitionCount = 5;
    internal const int Concurrency = 16;
    internal const int RawLimit = 1000;
    internal const int MaxSamples = 10000;
    internal const int MaxWindows = 1000;
    internal const double AverageTolerance = 1e-12;
    internal const long EpochUtcTicks = 639028224000000000;
    internal const int WindowWidthMinutes = 3;
    internal const int ItemsPerGroup = 16;
    internal const int TiedItems = 4;
    internal const int GroupMinutes = 5;
    internal const int RangeGroups = 224;
    internal const int RangeFromMinute = 1;
    internal const int RangeMinutes = 160;
    internal const int LatestMinute = 3;
    internal const int ValuePeriod = 31;
    internal const int ValueCenter = 15;
    internal const int ItemValueCenter = 8;
    internal const int QuarterScale = 4;
    internal const int AppendEpochDays = 10;
    internal const int SeedBatchSize = 256;
    internal const int CommandIdBytes = 16;
    internal const int GroupCount = SampleCount / ItemsPerGroup;
    internal const int SeedBatchCount = SampleCount / SeedBatchSize;
    internal const int TimestampCount = SampleCount / TiedItems;
    internal const int ReadbackGroups = SeedBatchSize / ItemsPerGroup;
    internal const int SeedReadbackMinutes = ReadbackGroups * GroupMinutes;
    internal const int SeedReadbackUntilMinute = (ReadbackGroups - 1) * GroupMinutes + LatestMinute;
    internal const int AppendReadbackCount = (OperationCount + RawLimit - 1) / RawLimit;
    internal const string SeedEventPrefix = "s-";
    internal const string AppendEventPrefix = "a-";
    internal const string SeedEventFormat = "D6";
    internal const string AppendEventFormat = "D5";
    internal const string WarmSeriesPrefix = "warm-r";
    internal const string MeasuredSeriesPrefix = "measured-r";
    internal const string WarmupPhase = "warmup";
    internal const string MeasuredPhase = "measured";
    internal const string CommandSeparator = ":";
    internal const string GuidFormat = "N";
    internal static readonly DateTimeOffset Epoch = new(EpochUtcTicks, TimeSpan.Zero);
    [ImmutableTemporalData]
    internal static readonly TimeSpan WindowWidth = TimeSpan.FromMinutes(WindowWidthMinutes);
}
