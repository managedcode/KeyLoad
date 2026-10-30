namespace KeyLoad.ClusterTests.DocumentStorage;

/// <summary>Local operation timings are diagnostic evidence and carry no public comparison qualification.</summary>
internal sealed record DocumentPathDiagnosticPhase(string Name, int Operations, double ElapsedSeconds, double OperationsPerSecond);
