namespace KeyLoad.Comparisons;

/// <summary>One deterministic execution operation and its exact input.</summary>
public sealed record DocumentScheduledOperation(Scenario Operation, BenchmarkDocument Input);
