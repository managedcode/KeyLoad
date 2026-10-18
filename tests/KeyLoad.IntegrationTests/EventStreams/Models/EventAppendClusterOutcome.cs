namespace KeyLoad.IntegrationTests.Features.EventStreams;

/// <summary>Actual decoded public result evidence; no stored authority or synthetic receipt.</summary>
internal sealed record EventAppendClusterOutcome(CommitReceipt? Receipt, ErrorCode? Error, string? Detail, string? ProblemJson);
