namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSubnormalRf3Corpus
{
    private const string HealthyId = "chunk-subnormal-after-cold";
    internal const long EmptySequence = 0;

    internal static SampleData First(SampleChunkRf3Scenario scenario)
        => scenario.First with { Value = double.Epsilon };
    internal static SampleData Equal(SampleChunkRf3Scenario scenario)
        => scenario.Equal with { Value = -double.Epsilon };
    internal static SampleData Healthy(SampleChunkRf3Scenario scenario)
        => new(HealthyId, scenario.From.AddTicks(SampleChunkRf3Protocol.CorrectedSequence), double.Epsilon);

    internal static SampleRecord[] Initial(SampleChunkRf3Scenario scenario) =>
    [
        new(TimeSeriesRf3Scenario.Series, First(scenario), SampleChunkRf3Protocol.FirstSequence,
            TimeSeriesRf3Scenario.PrivateTags),
        new(TimeSeriesRf3Scenario.Series, Equal(scenario), SampleChunkRf3Protocol.InitialSequence,
            TimeSeriesRf3Scenario.PrivateTags)
    ];

    internal static SampleRecord[] Corrected(SampleChunkRf3Scenario scenario) =>
    [
        .. Initial(scenario),
        new(TimeSeriesRf3Scenario.Series, scenario.Late, SampleChunkRf3Protocol.CorrectedSequence,
            TimeSeriesRf3Scenario.PrivateTags)
    ];

    internal static SampleRecord[] HealthyRecords(SampleChunkRf3Scenario scenario) =>
    [new(TimeSeriesRf3Scenario.EmptySeries, Healthy(scenario), SampleChunkRf3Protocol.FirstSequence,
        TimeSeriesRf3Scenario.PrivateTags)];
}
