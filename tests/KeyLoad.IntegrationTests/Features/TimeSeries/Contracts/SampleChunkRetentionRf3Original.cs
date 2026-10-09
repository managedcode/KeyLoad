namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkRetentionRf3Original(SampleChunkRf3Scenario Scenario,
    CommandRequest Append, CommitReceipt Receipt);
