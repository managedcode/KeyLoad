namespace KeyLoad.ClusterTests.TimeSeries;

internal sealed record SampleChunkRetentionClusterOriginal(SampleChunkClusterScenario Scenario,
    CommandRequest Append, CommitReceipt Receipt);
