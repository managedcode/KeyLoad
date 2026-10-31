namespace KeyLoad.ComparisonHost.Measurement;

/// <summary>The result file of one benchmark job: one database, one type, one node count.</summary>
internal sealed record BenchmarkResult(string Database, string Type, int Nodes, string? Image, string? Commit, long? RunId,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, int Records, int Operations, int Clients, int CaseLimitMinutes,
    string? Error,
    IReadOnlyList<CaseResult> Cases);
