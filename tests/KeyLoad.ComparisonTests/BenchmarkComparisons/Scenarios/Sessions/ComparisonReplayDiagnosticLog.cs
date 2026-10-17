namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal readonly record struct ComparisonReplayDiagnosticLog(int Key, int Voters, long Critical, long Forward,
    long Read, long Data, long NodeMaximum)
{
    internal const int SlotCount = 13;
    internal static int MaximumProtectedBytes => NativeExecutionPolicyFixture.Harness().Value.MaximumProtectedReplayLogBytes;
    internal static bool TryRead(string original, out ComparisonReplayDiagnosticLog record)
        => ComparisonReplayDiagnosticLogReader.TryRead(original, out record);

    internal bool Matches(ComparisonReplayDiagnosticLog configuration) => Key != 0 && configuration.Key == 0
        && Voters == configuration.Voters && NodeMaximum == configuration.NodeMaximum
        && Critical <= configuration.Critical && Forward <= configuration.Forward && Read <= configuration.Read
        && Data <= configuration.Data && Selected((Key - 1) % 4) == configuration.Selected((Key - 1) % 4);

    private long Selected(int pool) => pool switch
    {
        0 => Critical,
        1 => Forward,
        2 => Read,
        3 => Data,
        _ => throw new ArgumentOutOfRangeException(nameof(pool))
    };
}
