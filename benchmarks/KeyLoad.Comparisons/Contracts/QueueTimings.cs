namespace KeyLoad.Comparisons;

/// <summary>Queue phase durations reported for one completed queue cycle.</summary>
/// <param name="EnqueueMs">Elapsed enqueue time in milliseconds.</param>
/// <param name="ReceiveMs">Elapsed receive time in milliseconds.</param>
/// <param name="AckMs">Elapsed acknowledgement time in milliseconds.</param>
public sealed record QueueTimings(double EnqueueMs, double ReceiveMs, double AckMs);
