namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkRf3Continuation(SampleChunkRf3Scenario Scenario,
    CommandRequest OriginalSeal, CommitReceipt OriginalReceipt, CommitReceipt Correction);
