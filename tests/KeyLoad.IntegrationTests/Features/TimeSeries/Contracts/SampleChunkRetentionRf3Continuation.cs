namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkRetentionRf3Continuation(SampleChunkRf3Scenario Original,
    CommandRequest OriginalSourceRequest, CommitReceipt OriginalSourceReceipt, Guid FreshWindowId,
    SampleData FreshSample, CommandRequest OriginalAppend, CommitReceipt OriginalAppendReceipt);
