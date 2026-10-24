namespace KeyLoad.IntegrationTests.TimeSeries;

internal sealed record SampleChunkRetentionClusterOriginal(SampleChunkClusterScenario Scenario,
    CommandRequest Append, CommitReceipt Receipt);
