namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkRetentionClusterOriginal(SampleChunkClusterScenario Scenario,
    CommandRequest Append, CommitReceipt Receipt);
