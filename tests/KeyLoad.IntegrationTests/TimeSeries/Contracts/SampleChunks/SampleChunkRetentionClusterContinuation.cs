namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkRetentionClusterContinuation(SampleChunkClusterScenario Original,
    CommandRequest OriginalSourceRequest, CommitReceipt OriginalSourceReceipt, Guid FreshWindowId,
    SampleData FreshSample, CommandRequest OriginalAppend, CommitReceipt OriginalAppendReceipt, SampleChunkRetainedIdentityClusterState RetainedIdentity);
