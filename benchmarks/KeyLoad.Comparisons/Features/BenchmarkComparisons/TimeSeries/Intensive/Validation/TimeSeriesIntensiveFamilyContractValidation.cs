using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveFamilyContractKeys
{
    internal const string SchemaVersion = "schemaVersion";
    internal const string Family = "family";
    internal const string EvidenceProfile = "evidenceProfile";
    internal const string Targets = "targets";
    internal const string NodeCounts = "nodeCounts";
    internal const string Scenarios = "scenarios";
    internal const string SampleCount = "sampleCount";
    internal const string OperationCount = "operationCount";
    internal const string WarmupCount = "warmupCount";
    internal const string Repetitions = "repetitions";
    internal const string Concurrency = "concurrency";
    internal const string OperationTimeoutSeconds = "operationTimeoutSeconds";
    internal const string CellTimeoutMinutes = "cellTimeoutMinutes";
    internal const string PreflightTimeoutMinutes = "preflightTimeoutMinutes";
    internal const string TeardownTimeoutSeconds = "teardownTimeoutSeconds";
    internal const string ClientDecodedResponseLimit = "clientDecodedResponseLimit";
    internal const string RawAttemptCount = "rawAttemptCount";
    internal const string ExpectedPreflightCells = "expectedPreflightCells";
    internal const string ExpectedIntensiveCells = "expectedIntensiveCells";
    internal const string TimestampUnit = "timestampUnit";
    internal const string ThroughputDenominator = "throughputDenominator";
    internal const string PercentileMethod = "percentileMethod";
    internal const string MeasuredRetries = "measuredRetries";
    internal const string QuorumAcknowledgements = "quorumAcknowledgements";
    internal const string DataCopies = "dataCopies";
}

internal static class TimeSeriesIntensiveFamilyContractErrors
{
    internal const string TooLarge = "TimeSeriesIntensiveFamilyContractTooLarge";
    internal const string Drift = "TimeSeriesIntensiveFamilyContractDrift";
    internal const string ObjectRequired = "TimeSeriesIntensiveFamilyContractObjectRequired";
    internal const string PropertyInvalid = "TimeSeriesIntensiveFamilyContractPropertyInvalid";
    internal const string PropertyMissing = "TimeSeriesIntensiveFamilyContractPropertyMissing";
    internal const string StringInvalid = "TimeSeriesIntensiveFamilyContractStringInvalid";
    internal const string IntegerInvalid = "TimeSeriesIntensiveFamilyContractIntegerInvalid";
    internal const string ArrayInvalid = "TimeSeriesIntensiveFamilyContractArrayInvalid";
    internal const string ArrayValueInvalid = "TimeSeriesIntensiveFamilyContractArrayValueInvalid";
    internal const string TargetInvalid = "TimeSeriesIntensiveFamilyTargetInvalid";
}

internal static class TimeSeriesIntensiveFamilyContractFacts
{
    private const int SingleItemCount = 1;
    private const int PairMemberCount = 2;
    private const int ThirdContractOrdinal = 3;
    private const int SingleNodeTopology = 1;

    internal const int MaximumBytes = 16384;
    internal const int MaximumDepth = 4;
    internal const int SchemaVersion = 1;
    internal const int SampleCount = 4096;
    internal const int OperationCount = 10000;
    internal const int WarmupCount = 256;
    internal const int Repetitions = 5;
    internal const int Concurrency = 16;
    internal const int OperationTimeoutSeconds = 30;
    internal const int CellTimeoutMinutes = 90;
    internal const int PreflightTimeoutMinutes = 30;
    internal const int TeardownTimeoutSeconds = 30;
    internal const int ClientDecodedResponseLimit = 16;
    internal const int RawAttemptCount = 50000;
    internal const int ExpectedPreflightCells = 4;
    internal const int ExpectedIntensiveCells = 20;
    internal const int MeasuredRetries = 0;
    internal const string Family = "timeseries-intensive";
    internal const string EvidenceProfile = "intensive-timeseries-4096-c16";
    internal const string TimestampUnit = "stopwatch-ticks";
    internal const string ThroughputDenominator = "validation-inclusive-wall";
    internal const string PercentileMethod = "nearest-rank-per-repetition";

    internal static readonly ImmutableArray<TimeSeriesIntensiveTargetKind> Targets =
        [TimeSeriesIntensiveTargetKind.KeyLoad, TimeSeriesIntensiveTargetKind.TimescaleDB];
    internal static readonly ImmutableArray<int> NodeCounts = [SingleItemCount, ThirdContractOrdinal];
    internal static readonly ImmutableArray<TimeSeriesIntensiveScenario> Scenarios =
    [
        TimeSeriesIntensiveScenario.Append, TimeSeriesIntensiveScenario.RawRangeRead,
        TimeSeriesIntensiveScenario.Latest, TimeSeriesIntensiveScenario.Aggregate, TimeSeriesIntensiveScenario.Windows,
    ];
    internal static readonly ImmutableArray<int> QuorumAcknowledgements = [SingleItemCount, PairMemberCount];
    internal static readonly ImmutableArray<int> DataCopies = [SingleNodeTopology, ThirdContractOrdinal];
}

internal static class TimeSeriesIntensiveFamilyContractValidation
{
    private static readonly string[] RequiredProperties =
    [
        TimeSeriesIntensiveFamilyContractKeys.SchemaVersion, TimeSeriesIntensiveFamilyContractKeys.Family,
        TimeSeriesIntensiveFamilyContractKeys.EvidenceProfile, TimeSeriesIntensiveFamilyContractKeys.Targets,
        TimeSeriesIntensiveFamilyContractKeys.NodeCounts, TimeSeriesIntensiveFamilyContractKeys.Scenarios,
        TimeSeriesIntensiveFamilyContractKeys.SampleCount, TimeSeriesIntensiveFamilyContractKeys.OperationCount,
        TimeSeriesIntensiveFamilyContractKeys.WarmupCount, TimeSeriesIntensiveFamilyContractKeys.Repetitions,
        TimeSeriesIntensiveFamilyContractKeys.Concurrency, TimeSeriesIntensiveFamilyContractKeys.OperationTimeoutSeconds,
        TimeSeriesIntensiveFamilyContractKeys.CellTimeoutMinutes, TimeSeriesIntensiveFamilyContractKeys.PreflightTimeoutMinutes,
        TimeSeriesIntensiveFamilyContractKeys.TeardownTimeoutSeconds, TimeSeriesIntensiveFamilyContractKeys.ClientDecodedResponseLimit,
        TimeSeriesIntensiveFamilyContractKeys.RawAttemptCount, TimeSeriesIntensiveFamilyContractKeys.ExpectedPreflightCells,
        TimeSeriesIntensiveFamilyContractKeys.ExpectedIntensiveCells, TimeSeriesIntensiveFamilyContractKeys.TimestampUnit,
        TimeSeriesIntensiveFamilyContractKeys.ThroughputDenominator, TimeSeriesIntensiveFamilyContractKeys.PercentileMethod,
        TimeSeriesIntensiveFamilyContractKeys.MeasuredRetries, TimeSeriesIntensiveFamilyContractKeys.QuorumAcknowledgements,
        TimeSeriesIntensiveFamilyContractKeys.DataCopies,
    ];

    internal static TimeSeriesIntensiveFamilyContract Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > TimeSeriesIntensiveFamilyContractFacts.MaximumBytes)
        {
            throw new InvalidDataException(TimeSeriesIntensiveFamilyContractErrors.TooLarge);
        }
        using var document = JsonDocument.Parse(bytes,
            new JsonDocumentOptions { MaxDepth = TimeSeriesIntensiveFamilyContractFacts.MaximumDepth });
        var properties = ReadProperties(document.RootElement);
        var contract = TimeSeriesIntensiveFamilyContract.Create(
            ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.SchemaVersion), ReadString(properties, TimeSeriesIntensiveFamilyContractKeys.Family),
            ReadString(properties, TimeSeriesIntensiveFamilyContractKeys.EvidenceProfile), ReadEnumArray<TimeSeriesIntensiveTargetKind>(properties, TimeSeriesIntensiveFamilyContractKeys.Targets),
            ReadIntegerArray(properties, TimeSeriesIntensiveFamilyContractKeys.NodeCounts), ReadEnumArray<TimeSeriesIntensiveScenario>(properties, TimeSeriesIntensiveFamilyContractKeys.Scenarios),
            ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.SampleCount), ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.OperationCount),
            ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.WarmupCount), ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.Repetitions),
            ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.Concurrency), ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.OperationTimeoutSeconds),
            ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.CellTimeoutMinutes), ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.PreflightTimeoutMinutes),
            ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.TeardownTimeoutSeconds), ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.ClientDecodedResponseLimit),
            ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.RawAttemptCount), ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.ExpectedPreflightCells),
            ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.ExpectedIntensiveCells), ReadString(properties, TimeSeriesIntensiveFamilyContractKeys.TimestampUnit),
            ReadString(properties, TimeSeriesIntensiveFamilyContractKeys.ThroughputDenominator), ReadString(properties, TimeSeriesIntensiveFamilyContractKeys.PercentileMethod),
            ReadInteger(properties, TimeSeriesIntensiveFamilyContractKeys.MeasuredRetries), ReadIntegerArray(properties, TimeSeriesIntensiveFamilyContractKeys.QuorumAcknowledgements),
            ReadIntegerArray(properties, TimeSeriesIntensiveFamilyContractKeys.DataCopies), bytes.Span);
        Validate(contract);
        return contract;
    }

    internal static void Validate(TimeSeriesIntensiveFamilyContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (contract.SchemaVersion != TimeSeriesIntensiveFamilyContractFacts.SchemaVersion || contract.Family != TimeSeriesIntensiveFamilyContractFacts.Family
            || contract.EvidenceProfile != TimeSeriesIntensiveFamilyContractFacts.EvidenceProfile || !contract.Targets.SequenceEqual(TimeSeriesIntensiveFamilyContractFacts.Targets)
            || !contract.NodeCounts.SequenceEqual(TimeSeriesIntensiveFamilyContractFacts.NodeCounts) || !contract.Scenarios.SequenceEqual(TimeSeriesIntensiveFamilyContractFacts.Scenarios)
            || contract.SampleCount != TimeSeriesIntensiveFamilyContractFacts.SampleCount || contract.OperationCount != TimeSeriesIntensiveFamilyContractFacts.OperationCount
            || contract.WarmupCount != TimeSeriesIntensiveFamilyContractFacts.WarmupCount || contract.Repetitions != TimeSeriesIntensiveFamilyContractFacts.Repetitions
            || contract.Concurrency != TimeSeriesIntensiveFamilyContractFacts.Concurrency || contract.OperationTimeoutSeconds != TimeSeriesIntensiveFamilyContractFacts.OperationTimeoutSeconds
            || contract.CellTimeoutMinutes != TimeSeriesIntensiveFamilyContractFacts.CellTimeoutMinutes
            || contract.PreflightTimeoutMinutes != TimeSeriesIntensiveFamilyContractFacts.PreflightTimeoutMinutes
            || contract.TeardownTimeoutSeconds != TimeSeriesIntensiveFamilyContractFacts.TeardownTimeoutSeconds
            || contract.ClientDecodedResponseLimit != TimeSeriesIntensiveFamilyContractFacts.ClientDecodedResponseLimit
            || contract.RawAttemptCount != TimeSeriesIntensiveFamilyContractFacts.RawAttemptCount
            || contract.ExpectedPreflightCells != TimeSeriesIntensiveFamilyContractFacts.ExpectedPreflightCells
            || contract.ExpectedIntensiveCells != TimeSeriesIntensiveFamilyContractFacts.ExpectedIntensiveCells
            || contract.TimestampUnit != TimeSeriesIntensiveFamilyContractFacts.TimestampUnit || contract.ThroughputDenominator != TimeSeriesIntensiveFamilyContractFacts.ThroughputDenominator
            || contract.PercentileMethod != TimeSeriesIntensiveFamilyContractFacts.PercentileMethod || contract.MeasuredRetries != TimeSeriesIntensiveFamilyContractFacts.MeasuredRetries
            || !contract.QuorumAcknowledgements.SequenceEqual(TimeSeriesIntensiveFamilyContractFacts.QuorumAcknowledgements)
            || !contract.DataCopies.SequenceEqual(TimeSeriesIntensiveFamilyContractFacts.DataCopies))
        {
            throw new InvalidDataException(TimeSeriesIntensiveFamilyContractErrors.Drift);
        }
    }

    private static Dictionary<string, JsonElement> ReadProperties(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(TimeSeriesIntensiveFamilyContractErrors.ObjectRequired);
        }
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!RequiredProperties.Contains(property.Name, StringComparer.Ordinal)
                || !properties.TryAdd(property.Name, property.Value))
            {
                throw new InvalidDataException(TimeSeriesIntensiveFamilyContractErrors.PropertyInvalid);
            }
        }
        if (properties.Count != RequiredProperties.Length)
        {
            throw new InvalidDataException(TimeSeriesIntensiveFamilyContractErrors.PropertyMissing);
        }
        return properties;
    }

    private static string ReadString(Dictionary<string, JsonElement> properties, string name)
        => properties[name].ValueKind == JsonValueKind.String && properties[name].GetString() is { } value
            ? value : throw new InvalidDataException(TimeSeriesIntensiveFamilyContractErrors.StringInvalid);

    private static int ReadInteger(Dictionary<string, JsonElement> properties, string name)
        => properties[name].ValueKind == JsonValueKind.Number && properties[name].TryGetInt32(out var value)
            ? value : throw new InvalidDataException(TimeSeriesIntensiveFamilyContractErrors.IntegerInvalid);

    private static ImmutableArray<int> ReadIntegerArray(Dictionary<string, JsonElement> properties, string name)
    {
        if (properties[name].ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(TimeSeriesIntensiveFamilyContractErrors.ArrayInvalid);
        }
        var values = ImmutableArray.CreateBuilder<int>();
        foreach (var item in properties[name].EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var value))
            {
                throw new InvalidDataException(TimeSeriesIntensiveFamilyContractErrors.ArrayValueInvalid);
            }
            values.Add(value);
        }
        return values.ToImmutable();
    }

    private static ImmutableArray<TEnum> ReadEnumArray<TEnum>(Dictionary<string, JsonElement> properties, string name)
        where TEnum : struct, Enum
    {
        if (properties[name].ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(TimeSeriesIntensiveFamilyContractErrors.ArrayInvalid);
        }
        var values = ImmutableArray.CreateBuilder<TEnum>();
        foreach (var item in properties[name].EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } text
                || !Enum.TryParse<TEnum>(text, out var value) || !Enum.IsDefined(value) || text != value.ToString())
            {
                throw new InvalidDataException(TimeSeriesIntensiveFamilyContractErrors.ArrayValueInvalid);
            }
            values.Add(value);
        }
        return values.ToImmutable();
    }
}
