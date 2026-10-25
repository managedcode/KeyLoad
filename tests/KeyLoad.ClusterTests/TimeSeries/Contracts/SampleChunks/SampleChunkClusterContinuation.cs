namespace KeyLoad.ClusterTests.TimeSeries;

internal sealed record SampleChunkClusterContinuation(SampleChunkClusterScenario Scenario,
    CommandRequest OriginalSeal, CommitReceipt OriginalReceipt, CommitReceipt Correction);
