namespace KeyLoad.Comparisons;

/// <summary>Preserves one actual isolated worker report and its exact GitHub cell identity.</summary>
/// <param name="SchemaVersion">The envelope schema version.</param>
/// <param name="Worker">The single authenticated workflow cell identity.</param>
/// <param name="Disposition">Measured worker or explicitly unsupported native topology.</param>
/// <param name="Reason">The precise unsupported native topology reason, otherwise null.</param>
/// <param name="Report">The original per-host report, null only for unsupported native topology.</param>
public sealed record IsolatedComparisonReport(int SchemaVersion, IsolatedComparisonWorker Worker,
    string Disposition, string? Reason, ComparisonReport? Report);

/// <summary>Identifies one cell in a source/run/attempt-bound isolated comparison cohort.</summary>
/// <param name="Target">The exact target name.</param>
/// <param name="NodeCount">The requested actual native members.</param>
/// <param name="Scenario">The one scenario.</param>
/// <param name="Profile">The canonical intensive profile.</param>
/// <param name="SourceRevision">The measured source SHA.</param>
/// <param name="RunId">The actual GitHub run.</param>
/// <param name="Attempt">The actual run attempt.</param>
/// <param name="Repository">The owning repository.</param>
/// <param name="Ref">The measured reference.</param>
/// <param name="Workflow">The actual producer workflow.</param>
/// <param name="JobId">The exact native case job ID.</param>
public sealed record IsolatedComparisonWorker(string Target, int NodeCount, Scenario Scenario, string Profile,
    string SourceRevision, long RunId, int Attempt, string Repository, string Ref, string Workflow, long JobId);
