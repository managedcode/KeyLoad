namespace KeyLoad.ComparisonHost.Measurement;

/// <summary>The measured outcome of one benchmark case.</summary>
/// <param name="Id">The case identifier shared by every database of the type.</param>
/// <param name="Status"><see cref="Passed"/> or <see cref="Failed"/>.</param>
/// <param name="Operations">Units of work attempted (records for loads, requests otherwise).</param>
/// <param name="Errors">Units of work that failed.</param>
/// <param name="Seconds">Wall time of the case.</param>
/// <param name="OpsPerSecond">Successful units per second of wall time.</param>
/// <param name="P50Ms">Median call latency; zero when no call succeeded.</param>
/// <param name="P95Ms">95th percentile call latency.</param>
/// <param name="P99Ms">99th percentile call latency.</param>
/// <param name="BatchSize">Units per call; latencies are per call.</param>
/// <param name="Error">The first error, when the case failed.</param>
internal sealed record CaseResult(string Id, string Status, long Operations, long Errors, double Seconds, double OpsPerSecond,
    double P50Ms, double P95Ms, double P99Ms, int BatchSize, string? Error)
{
    internal const string Passed = "passed";
    internal const string Failed = "failed";
    private const int MaximumErrorLength = 600;

    internal bool IsFailed => Status == Failed;

    internal static CaseResult Measured(string id, long units, int batchSize, double[] latencies, FailureCounter failures,
        TimeSpan elapsed)
    {
        var succeeded = latencies.Where(value => !double.IsNaN(value)).Order().ToArray();
        var seconds = elapsed.TotalSeconds;
        var errors = failures.Units;
        var throughput = seconds > 0 ? (units - errors) / seconds : 0;
        return new(id, errors == 0 ? Passed : Failed, units, errors, seconds, throughput,
            Percentile(succeeded, 0.50), Percentile(succeeded, 0.95), Percentile(succeeded, 0.99), batchSize,
            errors == 0 ? null : failures.First);
    }

    internal static CaseResult Failure(string id, Exception error, TimeSpan elapsed)
        => new(id, Failed, 0, 0, elapsed.TotalSeconds, 0, 0, 0, 0, 1, Describe(error));

    /// <summary>Marks a measured case failed when its after-the-fact verification found a problem.</summary>
    internal CaseResult WithVerificationError(string error, long errors)
        => this with { Status = Failed, Errors = Errors + errors, Error = Error ?? error };

    /// <summary>Joins two sequential phases of one case, such as vertices then edges.</summary>
    internal CaseResult Then(CaseResult next) => new(Id, IsFailed || next.IsFailed ? Failed : Passed,
        Operations + next.Operations, Errors + next.Errors, Seconds + next.Seconds,
        Seconds + next.Seconds > 0 ? (Operations + next.Operations - Errors - next.Errors) / (Seconds + next.Seconds) : 0,
        P50Ms, P95Ms, P99Ms, BatchSize, Error ?? next.Error);

    internal static string Describe(Exception error)
    {
        var text = error.GetType().Name + ": " + error.Message;
        return text.Length <= MaximumErrorLength ? text : text[..MaximumErrorLength];
    }

    private static double Percentile(double[] sorted, double quantile)
        => sorted.Length == 0 ? 0 : sorted[Math.Max(0, (int)Math.Ceiling(quantile * sorted.Length) - 1)];
}
