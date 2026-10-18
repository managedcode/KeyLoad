namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSubnormalClusterCorpus
{
    private const string HealthyId = "chunk-subnormal-after-cold";
    internal const long EmptySequence = 0;

    internal static SampleData First(SampleChunkClusterScenario scenario)
        => scenario.First with
        {
            Value = double.Epsilon
        };
    internal static SampleData Equal(SampleChunkClusterScenario scenario)
        => scenario.Equal with
        {
            Value = -double.Epsilon
        };
    internal static SampleData Healthy(SampleChunkClusterScenario scenario)
        => new(HealthyId, scenario.From.AddTicks(SampleChunkClusterProtocol.CorrectedSequence), double.Epsilon);

    internal static SampleRecord[] Initial(SampleChunkClusterScenario scenario) =>
    [
        new(TimeSeriesClusterScenario.Series, First(scenario), SampleChunkClusterProtocol.FirstSequence,
            TimeSeriesClusterScenario.PrivateTags),
        new(TimeSeriesClusterScenario.Series, Equal(scenario), SampleChunkClusterProtocol.InitialSequence,
            TimeSeriesClusterScenario.PrivateTags)
    ];

    internal static SampleRecord[] Corrected(SampleChunkClusterScenario scenario) =>
    [
        .. Initial(scenario),
        new(TimeSeriesClusterScenario.Series, scenario.Late, SampleChunkClusterProtocol.CorrectedSequence,
            TimeSeriesClusterScenario.PrivateTags)
    ];

    internal static SampleRecord[] HealthyRecords(SampleChunkClusterScenario scenario) =>
    [new(TimeSeriesClusterScenario.EmptySeries, Healthy(scenario), SampleChunkClusterProtocol.FirstSequence,
        TimeSeriesClusterScenario.PrivateTags)];
}
