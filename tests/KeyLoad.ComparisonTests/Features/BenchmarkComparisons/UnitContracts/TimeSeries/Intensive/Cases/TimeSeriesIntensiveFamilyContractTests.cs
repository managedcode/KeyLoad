using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveFamilyContractTests
{
    [Test]
    public async Task EmbeddedContractHasExactFrozenBytesAndValues()
    {
        var contract = TimeSeriesIntensiveFamilyContract.Current;
        await TimeSeriesIntensiveFamilyContractTestAssertions.VerifyFrozenContractAsync(contract);
    }

    [Test]
    public async Task PlanContainsExactlySixPreflightsAndThirtyIntensiveCells()
    {
        var contract = TimeSeriesIntensiveFamilyContract.Current;
        var cells = TimeSeriesIntensiveFamilyPlan.Create(contract);
        await TimeSeriesIntensiveFamilyContractTestAssertions.VerifyPlanAsync(cells, contract);
    }

    [Test]
    public async Task EveryFrozenFieldRejectsAChangedValue()
    {
        var json = Encoding.UTF8.GetString(TimeSeriesIntensiveFamilyContractTestAssertions.EmbeddedBytes());
        foreach (var mutation in TimeSeriesIntensiveFamilyContractTestCases.FieldMutations)
        {
            var changed = TimeSeriesIntensiveFamilyContractTestAssertions.ReplaceOnce(json, mutation.Before, mutation.After);
            await Assert.That(TimeSeriesIntensiveFamilyContractTestAssertions.IsRejected(changed)).IsTrue();
        }
    }

    [Test]
    public async Task OrderedArraysRejectReorderingAndDuplicates()
    {
        var json = Encoding.UTF8.GetString(TimeSeriesIntensiveFamilyContractTestAssertions.EmbeddedBytes());
        foreach (var mutation in TimeSeriesIntensiveFamilyContractTestCases.ArrayMutations)
        {
            var changed = TimeSeriesIntensiveFamilyContractTestAssertions.ReplaceOnce(json, mutation.Before, mutation.After);
            await Assert.That(TimeSeriesIntensiveFamilyContractTestAssertions.IsRejected(changed)).IsTrue();
        }
    }

    [Test]
    public async Task ParserRejectsUnknownDuplicateMissingNullAndWrongTypedProperties()
    {
        var json = Encoding.UTF8.GetString(TimeSeriesIntensiveFamilyContractTestAssertions.EmbeddedBytes());
        foreach (var property in TimeSeriesIntensiveFamilyContractTestCases.PropertyNames)
        {
            await Assert.That(TimeSeriesIntensiveFamilyContractTestAssertions.IsRejected(
                TimeSeriesIntensiveFamilyContractTestAssertions.SetProperty(json, property, null))).IsTrue();
            await Assert.That(TimeSeriesIntensiveFamilyContractTestAssertions.IsRejected(
                TimeSeriesIntensiveFamilyContractTestAssertions.SetProperty(json, property, new JsonObject()))).IsTrue();
        }
        await Assert.That(TimeSeriesIntensiveFamilyContractTestAssertions.IsRejected(
            TimeSeriesIntensiveFamilyContractTestAssertions.WithUnknownProperty(json))).IsTrue();
        await Assert.That(TimeSeriesIntensiveFamilyContractTestAssertions.IsRejected(
            TimeSeriesIntensiveFamilyContractTestAssertions.WithDuplicateProperty(json))).IsTrue();
        foreach (var property in TimeSeriesIntensiveFamilyContractTestCases.PropertyNames)
        {
            await Assert.That(TimeSeriesIntensiveFamilyContractTestAssertions.IsRejected(
                TimeSeriesIntensiveFamilyContractTestAssertions.WithoutProperty(json, property))).IsTrue();
        }
        await Assert.That(TimeSeriesIntensiveFamilyContractTestAssertions.IsRejected(
            TimeSeriesIntensiveFamilyContractTestAssertions.WithUnknownChecksumProperty(json))).IsTrue();
    }

    [Test]
    public async Task ParserRejectsWrongPrimitiveAndArrayElementTypes()
    {
        var json = Encoding.UTF8.GetString(TimeSeriesIntensiveFamilyContractTestAssertions.EmbeddedBytes());
        var malformed = new[]
        {
            TimeSeriesIntensiveFamilyContractTestAssertions.SetProperty(json,
                TimeSeriesIntensiveFamilyContractTestKeys.SampleCount, JsonValue.Create("4096")),
            TimeSeriesIntensiveFamilyContractTestAssertions.SetProperty(json,
                TimeSeriesIntensiveFamilyContractTestKeys.SampleCount, JsonValue.Create(4096.5)),
            TimeSeriesIntensiveFamilyContractTestAssertions.SetProperty(json,
                TimeSeriesIntensiveFamilyContractTestKeys.SampleCount, JsonValue.Create(true)),
            TimeSeriesIntensiveFamilyContractTestAssertions.SetArrayItem(json,
                TimeSeriesIntensiveFamilyContractTestKeys.Targets, 0, JsonValue.Create("keyload")),
            TimeSeriesIntensiveFamilyContractTestAssertions.SetArrayItem(json,
                TimeSeriesIntensiveFamilyContractTestKeys.Targets, 0, JsonValue.Create(0)),
            TimeSeriesIntensiveFamilyContractTestAssertions.SetArrayItem(json,
                TimeSeriesIntensiveFamilyContractTestKeys.Scenarios, 0, JsonValue.Create(0)),
            TimeSeriesIntensiveFamilyContractTestAssertions.SetArrayItem(json,
                TimeSeriesIntensiveFamilyContractTestKeys.NodeCounts, 0, JsonValue.Create(1.5)),
            TimeSeriesIntensiveFamilyContractTestAssertions.SetArrayItem(json,
                TimeSeriesIntensiveFamilyContractTestKeys.DataCopies, 0, JsonValue.Create("1")),
            TimeSeriesIntensiveFamilyContractTestAssertions.SetArrayItem(json,
                TimeSeriesIntensiveFamilyContractTestKeys.QuorumAcknowledgements, 0, null),
        };
        foreach (var value in malformed)
        {
            await Assert.That(TimeSeriesIntensiveFamilyContractTestAssertions.IsRejected(value)).IsTrue();
        }
    }

    [Test]
    public async Task ParserRejectsMalformedDeepAndOversizedJson()
    {
        var json = Encoding.UTF8.GetString(TimeSeriesIntensiveFamilyContractTestAssertions.EmbeddedBytes());
        await Assert.That(TimeSeriesIntensiveFamilyContractTestAssertions.IsRejected("{invalid")).IsTrue();
        var deep = TimeSeriesIntensiveFamilyContractTestAssertions.ReplaceOnce(json,
            TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(
                TimeSeriesIntensiveFamilyContractTestKeys.SchemaVersion, "1", "1").Before,
            TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(
                TimeSeriesIntensiveFamilyContractTestKeys.SchemaVersion, "1", "[[[[[1]]]]]").After);
        await Assert.That(TimeSeriesIntensiveFamilyContractTestAssertions.IsRejected(deep)).IsTrue();
        await Assert.That(TimeSeriesIntensiveFamilyContractTestAssertions.IsOversizedRejected(
            TimeSeriesIntensiveFamilyContractTestAssertions.EmbeddedBytes())).IsTrue();
    }
}

internal static class TimeSeriesIntensiveFamilyContractTestAssertions
{
    private const string ResourceName = "KeyLoad.Comparisons.TimeSeriesIntensiveContract";
    private const string ExpectedHash = "9b13ab99e8acb25ad11d08e394fd5af758553ae00bef0a52a858975ea58a5fbb";
    private const int MaximumBytes = 16384;

    internal static async Task VerifyFrozenContractAsync(TimeSeriesIntensiveFamilyContract value)
    {
        using var canonical = JsonDocument.Parse(EmbeddedBytes());
        await Assert.That(value.ContractSha256).IsEqualTo(ExpectedHash);
        await Assert.That(value.SchemaVersion).IsEqualTo(1);
        await Assert.That(value.Family).IsEqualTo("timeseries-intensive");
        await Assert.That(value.EvidenceProfile).IsEqualTo("intensive-timeseries-4096-c16");
        await Assert.That(value.Targets.SequenceEqual([TimeSeriesIntensiveTargetKind.KeyLoad, TimeSeriesIntensiveTargetKind.TimescaleDB])).IsTrue();
        await Assert.That(value.NodeCounts.SequenceEqual([1, 3])).IsTrue();
        await Assert.That(value.Scenarios.SequenceEqual([TimeSeriesIntensiveScenario.Append, TimeSeriesIntensiveScenario.RawRangeRead,
            TimeSeriesIntensiveScenario.Latest, TimeSeriesIntensiveScenario.Aggregate, TimeSeriesIntensiveScenario.Windows])).IsTrue();
        await Assert.That(value.SampleCount).IsEqualTo(4096);
        await Assert.That(value.OperationCount).IsEqualTo(10000);
        await Assert.That(value.WarmupCount).IsEqualTo(256);
        await Assert.That(value.Repetitions).IsEqualTo(5);
        await Assert.That(value.Concurrency).IsEqualTo(16);
        await Assert.That(value.OperationTimeoutSeconds).IsEqualTo(canonical.RootElement.GetProperty(TimeSeriesIntensiveFamilyContractTestKeys.OperationTimeoutSeconds).GetInt32());
        await Assert.That(value.CellTimeoutMinutes).IsEqualTo(90);
        await Assert.That(value.PreflightTimeoutMinutes).IsEqualTo(canonical.RootElement.GetProperty(TimeSeriesIntensiveFamilyContractTestKeys.PreflightTimeoutMinutes).GetInt32());
        await Assert.That(value.TeardownTimeoutSeconds).IsEqualTo(canonical.RootElement.GetProperty(TimeSeriesIntensiveFamilyContractTestKeys.TeardownTimeoutSeconds).GetInt32());
        await Assert.That(value.ClientDecodedResponseLimit).IsEqualTo(16);
        await Assert.That(value.RawAttemptCount).IsEqualTo(50000);
        await Assert.That(value.ExpectedPreflightCells).IsEqualTo(4);
        await Assert.That(value.ExpectedIntensiveCells).IsEqualTo(20);
        await Assert.That(value.TimestampUnit).IsEqualTo("stopwatch-ticks");
        await Assert.That(value.ThroughputDenominator).IsEqualTo("validation-inclusive-wall");
        await Assert.That(value.PercentileMethod).IsEqualTo("nearest-rank-per-repetition");
        await Assert.That(value.MeasuredRetries).IsEqualTo(0);
        await Assert.That(value.QuorumAcknowledgements.SequenceEqual([1, 2])).IsTrue();
        await Assert.That(value.DataCopies.SequenceEqual([1, 3])).IsTrue();
    }

    internal static async Task VerifyPlanAsync(TimeSeriesIntensiveFamilyCells cells,
        TimeSeriesIntensiveFamilyContract contract)
    {
        string[] preflightIds = ["ts-keyload-n1-preflight", "ts-keyload-n3-preflight",
            "ts-timescaledb-n1-preflight", "ts-timescaledb-n3-preflight"];
        var intensiveIds = ExpectedIntensiveIds();
        await Assert.That(cells.Preflight.Select(cell => cell.Id).SequenceEqual(preflightIds)).IsTrue();
        await Assert.That(cells.Intensive.Select(cell => cell.Id).SequenceEqual(intensiveIds)).IsTrue();
        await Assert.That(cells.Preflight.Select(cell => cell.Id).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(4);
        await Assert.That(cells.Intensive.Select(cell => cell.Id).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(20);
        await VerifySelectionsAsync(cells.Preflight, contract, intensive: false);
        await VerifySelectionsAsync(cells.Intensive, contract, intensive: true);
    }

    private static async Task VerifySelectionsAsync(ImmutableArray<TimeSeriesIntensiveFamilyCell> cells,
        TimeSeriesIntensiveFamilyContract contract, bool intensive)
    {
        var targets = new[] { TimeSeriesIntensiveTargetKind.KeyLoad, TimeSeriesIntensiveTargetKind.TimescaleDB };
        var scenarios = new[] { TimeSeriesIntensiveScenario.Append, TimeSeriesIntensiveScenario.RawRangeRead,
            TimeSeriesIntensiveScenario.Latest, TimeSeriesIntensiveScenario.Aggregate, TimeSeriesIntensiveScenario.Windows };
        for (var index = 0; index < cells.Length; index++)
        {
            var targetIndex = intensive ? index / 10 : index / 2;
            var nodeIndex = intensive ? index % 10 / 5 : index % 2;
            var selection = cells[index].Selection;
            await Assert.That(selection.Target).IsEqualTo(targets[targetIndex]);
            await Assert.That(selection.NodeCount).IsEqualTo(nodeIndex == 0 ? 1 : 3);
            await Assert.That(selection.Phase).IsEqualTo(intensive ? TimeSeriesIntensiveCellPhase.Intensive : TimeSeriesIntensiveCellPhase.Preflight);
            await Assert.That(selection.Scenario).IsEqualTo(intensive ? scenarios[index % 5] : null);
            await Assert.That(selection.EvidenceProfile).IsEqualTo(contract.EvidenceProfile);
            selection.Validate();
        }
    }

    private static string[] ExpectedIntensiveIds()
        =>
        [
            "ts-keyload-n1-Append", "ts-keyload-n1-RawRangeRead", "ts-keyload-n1-Latest", "ts-keyload-n1-Aggregate", "ts-keyload-n1-Windows",
            "ts-keyload-n3-Append", "ts-keyload-n3-RawRangeRead", "ts-keyload-n3-Latest", "ts-keyload-n3-Aggregate", "ts-keyload-n3-Windows",
            "ts-timescaledb-n1-Append", "ts-timescaledb-n1-RawRangeRead", "ts-timescaledb-n1-Latest", "ts-timescaledb-n1-Aggregate", "ts-timescaledb-n1-Windows",
            "ts-timescaledb-n3-Append", "ts-timescaledb-n3-RawRangeRead", "ts-timescaledb-n3-Latest", "ts-timescaledb-n3-Aggregate", "ts-timescaledb-n3-Windows",
        ];

    internal static byte[] EmbeddedBytes()
    {
        using var stream = typeof(TimeSeriesIntensiveFamilyContract).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Embedded family contract is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    internal static string ReplaceOnce(string source, string before, string after)
    {
        var index = source.IndexOf(before, StringComparison.Ordinal);
        if (index < 0 || source.IndexOf(before, index + before.Length, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException("Expected exactly one contract mutation marker.");
        }
        return string.Concat(source.AsSpan(0, index), after.AsSpan(), source.AsSpan(index + before.Length));
    }

    internal static string SetProperty(string source, string name, JsonNode? value)
    {
        var root = JsonNode.Parse(source)!.AsObject();
        root[name] = value?.DeepClone();
        return root.ToJsonString();
    }

    internal static string SetArrayItem(string source, string property, int index, JsonNode? value)
    {
        var root = JsonNode.Parse(source)!.AsObject();
        root[property]!.AsArray()[index] = value?.DeepClone();
        return root.ToJsonString();
    }

    internal static (string Before, string After) JsonMutation(string property, string beforeValue, string afterValue)
        => (string.Concat("\"", property, "\": ", beforeValue),
            string.Concat("\"", property, "\": ", afterValue));

    internal static string WithUnknownProperty(string source)
        => SetProperty(source, TimeSeriesIntensiveFamilyContractTestKeys.FutureField, JsonValue.Create(true));

    internal static string WithDuplicateProperty(string source)
    {
        var property = JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.SchemaVersion, "1", "1").Before;
        return ReplaceOnce(source, string.Concat(property, ","), string.Concat(property, ",\n  ", property, ","));
    }

    internal static string WithUnknownChecksumProperty(string source)
        => SetProperty(source, TimeSeriesIntensiveFamilyContractTestKeys.ContractSha256, JsonValue.Create(ExpectedHash));

    internal static string WithoutProperty(string source, string property)
    {
        var root = JsonNode.Parse(source)!.AsObject();
        root.Remove(property);
        return root.ToJsonString();
    }

    internal static bool IsRejected(string value)
    {
        try
        {
            _ = TimeSeriesIntensiveFamilyContract.Read(Encoding.UTF8.GetBytes(value));
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            return true;
        }
        return false;
    }

    internal static bool IsOversizedRejected(byte[] original)
    {
        var oversized = new byte[MaximumBytes + 1];
        original.CopyTo(oversized, 0);
        Array.Fill(oversized, (byte)' ', original.Length, oversized.Length - original.Length);
        return IsRejected(Encoding.UTF8.GetString(oversized));
    }
}

internal static class TimeSeriesIntensiveFamilyContractTestCases
{
    internal static readonly (string Before, string After)[] FieldMutations =
    [
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.SchemaVersion, "1", "2"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.Family, "\"timeseries-intensive\"", "\"other\""),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.EvidenceProfile, "\"intensive-timeseries-4096-c16\"", "\"other\""),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.SampleCount, "4096", "4095"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.OperationCount, "10000", "9999"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.WarmupCount, "256", "255"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.Repetitions, "5", "4"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.Concurrency, "16", "15"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.OperationTimeoutSeconds, "30", "29"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.CellTimeoutMinutes, "90", "89"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.PreflightTimeoutMinutes, "30", "29"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.TeardownTimeoutSeconds, "30", "29"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.ClientDecodedResponseLimit, "16", "15"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.RawAttemptCount, "50000", "49999"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.ExpectedPreflightCells, "4", "5"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.ExpectedIntensiveCells, "20", "29"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.TimestampUnit, "\"stopwatch-ticks\"", "\"ticks\""),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.ThroughputDenominator, "\"validation-inclusive-wall\"", "\"wall\""),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.PercentileMethod, "\"nearest-rank-per-repetition\"", "\"nearest-rank\""),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.MeasuredRetries, "0", "1"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.QuorumAcknowledgements, "[1, 2]", "[1, 1, 2]"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.DataCopies, "[1, 3]", "[1, 2]"),
    ];

    internal static readonly (string Before, string After)[] ArrayMutations =
    [
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.Targets,
            "[\"KeyLoad\", \"TimescaleDB\"]", "[\"TimescaleDB\", \"KeyLoad\"]"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.Targets,
            "[\"KeyLoad\", \"TimescaleDB\"]", "[\"KeyLoad\", \"KeyLoad\"]"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.NodeCounts, "[1, 3]", "[3, 2, 1]"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.NodeCounts, "[1, 3]", "[1, 2]"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.Scenarios,
            "[\"Append\", \"RawRangeRead\", \"Latest\", \"Aggregate\", \"Windows\"]",
            "[\"RawRangeRead\", \"Append\", \"Latest\", \"Aggregate\", \"Windows\"]"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.Scenarios,
            "[\"Append\", \"RawRangeRead\", \"Latest\", \"Aggregate\", \"Windows\"]",
            "[\"Append\", \"RawRangeRead\", \"Latest\", \"Aggregate\", \"Aggregate\"]"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.QuorumAcknowledgements, "[1, 2]", "[2, 1, 2]"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.QuorumAcknowledgements, "[1, 2]", "[1, 2, 1]"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.DataCopies, "[1, 3]", "[3, 2, 1]"),
        TimeSeriesIntensiveFamilyContractTestAssertions.JsonMutation(TimeSeriesIntensiveFamilyContractTestKeys.DataCopies, "[1, 3]", "[1, 2]"),
    ];

    internal static readonly string[] PropertyNames =
    [
        TimeSeriesIntensiveFamilyContractTestKeys.SchemaVersion, TimeSeriesIntensiveFamilyContractTestKeys.Family,
        TimeSeriesIntensiveFamilyContractTestKeys.EvidenceProfile, TimeSeriesIntensiveFamilyContractTestKeys.Targets,
        TimeSeriesIntensiveFamilyContractTestKeys.NodeCounts, TimeSeriesIntensiveFamilyContractTestKeys.Scenarios,
        TimeSeriesIntensiveFamilyContractTestKeys.SampleCount, TimeSeriesIntensiveFamilyContractTestKeys.OperationCount,
        TimeSeriesIntensiveFamilyContractTestKeys.WarmupCount, TimeSeriesIntensiveFamilyContractTestKeys.Repetitions,
        TimeSeriesIntensiveFamilyContractTestKeys.Concurrency, TimeSeriesIntensiveFamilyContractTestKeys.OperationTimeoutSeconds,
        TimeSeriesIntensiveFamilyContractTestKeys.CellTimeoutMinutes, TimeSeriesIntensiveFamilyContractTestKeys.PreflightTimeoutMinutes,
        TimeSeriesIntensiveFamilyContractTestKeys.TeardownTimeoutSeconds, TimeSeriesIntensiveFamilyContractTestKeys.ClientDecodedResponseLimit,
        TimeSeriesIntensiveFamilyContractTestKeys.RawAttemptCount, TimeSeriesIntensiveFamilyContractTestKeys.ExpectedPreflightCells,
        TimeSeriesIntensiveFamilyContractTestKeys.ExpectedIntensiveCells, TimeSeriesIntensiveFamilyContractTestKeys.TimestampUnit,
        TimeSeriesIntensiveFamilyContractTestKeys.ThroughputDenominator, TimeSeriesIntensiveFamilyContractTestKeys.PercentileMethod,
        TimeSeriesIntensiveFamilyContractTestKeys.MeasuredRetries, TimeSeriesIntensiveFamilyContractTestKeys.QuorumAcknowledgements,
        TimeSeriesIntensiveFamilyContractTestKeys.DataCopies,
    ];
}

internal static class TimeSeriesIntensiveFamilyContractTestKeys
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
    internal const string ContractSha256 = "contractSha256";
    internal const string FutureField = "futureField";
}
