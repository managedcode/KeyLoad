using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveFamilyContract
{
    private const string ResourceName = "KeyLoad.Comparisons.TimeSeriesIntensiveContract";
    private const string ResourceMissing = "Embedded family contract is missing.";

    private TimeSeriesIntensiveFamilyContract(int schemaVersion, string family, string evidenceProfile,
        ImmutableArray<TimeSeriesIntensiveTargetKind> targets, ImmutableArray<int> nodeCounts,
        ImmutableArray<TimeSeriesIntensiveScenario> scenarios, int sampleCount, int operationCount,
        int warmupCount, int repetitions, int concurrency, int operationTimeoutSeconds, int cellTimeoutMinutes,
        int preflightTimeoutMinutes, int teardownTimeoutSeconds, int clientDecodedResponseLimit, int rawAttemptCount,
        int expectedPreflightCells, int expectedIntensiveCells, string timestampUnit, string throughputDenominator,
        string percentileMethod, int measuredRetries, ImmutableArray<int> quorumAcknowledgements,
        ImmutableArray<int> dataCopies, string contractSha256)
    {
        SchemaVersion = schemaVersion;
        Family = family;
        EvidenceProfile = evidenceProfile;
        Targets = targets;
        NodeCounts = nodeCounts;
        Scenarios = scenarios;
        SampleCount = sampleCount;
        OperationCount = operationCount;
        WarmupCount = warmupCount;
        Repetitions = repetitions;
        Concurrency = concurrency;
        OperationTimeoutSeconds = operationTimeoutSeconds;
        CellTimeoutMinutes = cellTimeoutMinutes;
        PreflightTimeoutMinutes = preflightTimeoutMinutes;
        TeardownTimeoutSeconds = teardownTimeoutSeconds;
        ClientDecodedResponseLimit = clientDecodedResponseLimit;
        RawAttemptCount = rawAttemptCount;
        ExpectedPreflightCells = expectedPreflightCells;
        ExpectedIntensiveCells = expectedIntensiveCells;
        TimestampUnit = timestampUnit;
        ThroughputDenominator = throughputDenominator;
        PercentileMethod = percentileMethod;
        MeasuredRetries = measuredRetries;
        QuorumAcknowledgements = quorumAcknowledgements;
        DataCopies = dataCopies;
        ContractSha256 = contractSha256;
    }

    public int SchemaVersion { get; }
    public string Family { get; }
    public string EvidenceProfile { get; }
    public ImmutableArray<TimeSeriesIntensiveTargetKind> Targets { get; }
    public ImmutableArray<int> NodeCounts { get; }
    public ImmutableArray<TimeSeriesIntensiveScenario> Scenarios { get; }
    public int SampleCount { get; }
    public int OperationCount { get; }
    public int WarmupCount { get; }
    public int Repetitions { get; }
    public int Concurrency { get; }
    public int OperationTimeoutSeconds { get; }
    public int CellTimeoutMinutes { get; }
    public int PreflightTimeoutMinutes { get; }
    public int TeardownTimeoutSeconds { get; }
    public int ClientDecodedResponseLimit { get; }
    public int RawAttemptCount { get; }
    public int ExpectedPreflightCells { get; }
    public int ExpectedIntensiveCells { get; }
    public string TimestampUnit { get; }
    public string ThroughputDenominator { get; }
    public string PercentileMethod { get; }
    public int MeasuredRetries { get; }
    public ImmutableArray<int> QuorumAcknowledgements { get; }
    public ImmutableArray<int> DataCopies { get; }
    public string ContractSha256 { get; }

    internal static TimeSeriesIntensiveFamilyContract Current
    {
        get
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException(ResourceMissing);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return Read(memory.ToArray());
        }
    }

    internal static TimeSeriesIntensiveFamilyContract Read(ReadOnlyMemory<byte> bytes)
        => TimeSeriesIntensiveFamilyContractValidation.Read(bytes);

    internal static TimeSeriesIntensiveFamilyContract Create(int schemaVersion, string family, string evidenceProfile,
        ImmutableArray<TimeSeriesIntensiveTargetKind> targets, ImmutableArray<int> nodeCounts,
        ImmutableArray<TimeSeriesIntensiveScenario> scenarios, int sampleCount, int operationCount,
        int warmupCount, int repetitions, int concurrency, int operationTimeoutSeconds, int cellTimeoutMinutes,
        int preflightTimeoutMinutes, int teardownTimeoutSeconds, int clientDecodedResponseLimit, int rawAttemptCount,
        int expectedPreflightCells, int expectedIntensiveCells, string timestampUnit, string throughputDenominator,
        string percentileMethod, int measuredRetries, ImmutableArray<int> quorumAcknowledgements,
        ImmutableArray<int> dataCopies, ReadOnlySpan<byte> originalBytes)
        => new(schemaVersion, family, evidenceProfile, targets, nodeCounts, scenarios, sampleCount, operationCount,
            warmupCount, repetitions, concurrency, operationTimeoutSeconds, cellTimeoutMinutes, preflightTimeoutMinutes,
            teardownTimeoutSeconds, clientDecodedResponseLimit, rawAttemptCount, expectedPreflightCells,
            expectedIntensiveCells, timestampUnit, throughputDenominator, percentileMethod, measuredRetries,
            quorumAcknowledgements, dataCopies, Convert.ToHexStringLower(SHA256.HashData(originalBytes)));
}
