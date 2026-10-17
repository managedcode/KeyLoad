namespace KeyLoad.Comparisons;

/// <summary>Aggregates operation counts, throughput, latency, and optional resource timings.</summary>
/// <param name="Attempts">The number of attempted operations.</param>
/// <param name="Successes">The number of successful operations.</param>
/// <param name="Failures">The number of failed operations.</param>
/// <param name="ElapsedSeconds">The measured elapsed duration, in seconds.</param>
/// <param name="UsefulOperationsPerSecond">The useful completed operation rate per second.</param>
/// <param name="Latency">The overall operation latency percentiles.</param>
/// <param name="UniqueCompletedMessages">The number of unique completed messages.</param>
/// <param name="Enqueue">The enqueue latency percentiles, when measured.</param>
/// <param name="Receive">The receive latency percentiles, when measured.</param>
/// <param name="Ack">The acknowledgement latency percentiles, when measured.</param>
/// <param name="ClientResources">The load-generator process resource counters sampled during the measurement, when collected.</param>
public sealed record Measurement(int Attempts, int Successes, int Failures, double ElapsedSeconds,
    double UsefulOperationsPerSecond, Latencies Latency, int UniqueCompletedMessages,
    Latencies? Enqueue, Latencies? Receive, Latencies? Ack, ClientResources? ClientResources = null);
