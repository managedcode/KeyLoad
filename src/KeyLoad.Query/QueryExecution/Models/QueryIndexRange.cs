namespace KeyLoad.Query.QueryExecution;

/// <summary>One declared index and its encoded exclusive storage bounds.</summary>
internal sealed record QueryIndexRange(byte[] Prefix, byte[]? AfterKey, byte[]? UntilKey);
