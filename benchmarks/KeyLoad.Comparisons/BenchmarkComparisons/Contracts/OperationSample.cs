namespace KeyLoad.Comparisons;

/// <summary>Captures one operation's timing, outcome, payload, and queue measurements.</summary>
/// <param name="Operation">The operation index within the workload repetition.</param>
/// <param name="Worker">The worker index that executed the operation.</param>
/// <param name="StartedMs">The operation start timestamp in elapsed milliseconds.</param>
/// <param name="CompletedMs">The operation completion timestamp in elapsed milliseconds.</param>
/// <param name="Success">Whether the operation completed successfully.</param>
/// <param name="Error">The error description when the operation failed.</param>
/// <param name="PayloadBytes">The payload size associated with the operation, in bytes.</param>
/// <param name="CompletedMessageId">The identifier of a completed message, when applicable.</param>
/// <param name="Queue">The queue phase timings, when the operation measured a queue cycle.</param>
public sealed record OperationSample(int Operation, int Worker, double StartedMs, double CompletedMs,
    bool Success, string? Error, int PayloadBytes, string? CompletedMessageId, QueueTimings? Queue)
{
    /// <summary>Gets the elapsed operation latency in milliseconds.</summary>
    public double LatencyMs => CompletedMs - StartedMs;
}
