namespace KeyLoad.Comparisons;

/// <summary>The model operations retained by every database session.</summary>
public static class BenchmarkSessionModelCases
{
    /// <summary>Observed native model provisioning and acknowledged corpus preparation.</summary>
    public const string Prepare = "model-prepare";
    /// <summary>Exact cosine top-k search over the retained vector corpus.</summary>
    public const string Vector = "vector-exact";
    /// <summary>Confirmed publish, receive and manual acknowledgement.</summary>
    public const string Queue = "queue-cycle";
    /// <summary>Append with persisted identity, revision and complete payload verification.</summary>
    public const string Append = "stream-append";
    /// <summary>Read seeded events with exact identity, revision and payload verification.</summary>
    public const string Read = "stream-read";
    /// <summary>Concurrent append and stable event reads.</summary>
    public const string Mixed = "stream-mixed";

    /// <summary>Returns whether the current adapter must execute this model case.</summary>
    public static bool Required(string target, string caseId) => target switch
    {
        BenchmarkSessionContract.KeyLoad or BenchmarkSessionContract.PostgreSql => caseId is Prepare or Vector or Queue or Append or Read or Mixed,
        BenchmarkSessionContract.Qdrant or BenchmarkSessionContract.OpenSearch or BenchmarkSessionContract.Surreal => caseId is Prepare or Vector,
        BenchmarkSessionContract.RabbitMq => caseId is Prepare or Queue,
        BenchmarkSessionContract.Kurrent or BenchmarkSessionContract.Mongo => caseId is Prepare or Append or Read or Mixed,
        _ => false
    };
}
