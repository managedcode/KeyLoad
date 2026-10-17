namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkCorrectionPredicateRf3State(SampleChunkJobRevocationScenario Scenario,
    CommandRequest Correction, CommitReceipt Receipt)
{
    internal const string CorrectionTags = """{"visible":23}""";
    internal const string Pointer = "/visible";
    internal const string Hit = "23";
    internal const string Miss = "99";
    internal const int OneResult = 1;
    internal const int CompleteResults = 3;
    internal const int ColdCycles = 2;
    internal SampleChunkRf3Scenario Window => Scenario.Window;
    internal SampleRecord[] Rows =>
    [new(TimeSeriesRf3Scenario.Series, Window.First, 1L, TimeSeriesRf3Scenario.PrivateTags),
     new(TimeSeriesRf3Scenario.Series, Window.Equal, 2L, TimeSeriesRf3Scenario.PrivateTags),
     new(TimeSeriesRf3Scenario.Series, Window.Late, 3L, CorrectionTags)];
    internal OutboxStatus? Outbox
    {
        get; set;
    }
    internal SampleChunkWindowResult Expected(long generation, long revision, long cut, SampleRecord[] rows)
        => new(Window.WindowId, Window.From, Window.Until, generation, revision, 3L, null, [.. rows], cut);
}
