namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkClusterContinuation(SampleChunkClusterScenario Scenario,
    CommandRequest OriginalSeal, CommitReceipt OriginalReceipt, CommitReceipt Correction);
