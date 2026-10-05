namespace KeyLoad.Comparisons;

/// <summary>Records actual child-process settlement and the post-cancellation SDK read oracle.</summary>
/// <param name="Version">Proof schema version, fixed at one.</param>
/// <param name="Worker">The original isolated comparison worker identity.</param>
/// <param name="ProfileId">The exact selected scaled corpus profile.</param>
/// <param name="Rate">The actual offered point-read rate.</param>
/// <param name="Scenario">The actual selected scenario.</param>
/// <param name="DatasetRecords">The records loaded by the native target.</param>
/// <param name="DatasetSha256">The canonical generated corpus digest.</param>
/// <param name="Milestone">The first actual native completion milestone observed by the parent.</param>
/// <param name="Accounting">The original runner's disjoint disposition counts.</param>
/// <param name="ExecutionPolicy">The exact validated native options snapshot consumed by the run.</param>
/// <param name="CallerCancelled">Whether the runner's exact caller token was cancelled by the request.</param>
/// <param name="ProducerSettled">Whether the original arrival producer settled.</param>
/// <param name="NativeCallsSettled">Whether all original native operation tasks settled.</param>
/// <param name="SessionsClosed">Whether every original runner session closed.</param>
/// <param name="HealthyReadVerified">Whether the persisted post-cancellation SDK read matched the corpus.</param>
/// <param name="HealthyReadRevision">The actual positive SDK result revision.</param>
/// <param name="HealthyReadSha256">SHA-256 of the actual UTF-8 document JSON.</param>
/// <param name="HealthyReadSessionClosed">Whether the follow-up SDK session closed.</param>
public sealed record OpenLoopCancellationProofV1(int Version, IsolatedComparisonWorker Worker,
    string ProfileId, int Rate, Scenario Scenario, int DatasetRecords, string DatasetSha256,
    OpenLoopProgressV1 Milestone, OpenLoopOperationAccounting Accounting, OpenLoopExecutionPolicy ExecutionPolicy,
    bool CallerCancelled,
    bool ProducerSettled, bool NativeCallsSettled, bool SessionsClosed, bool HealthyReadVerified,
    long HealthyReadRevision, string HealthyReadSha256, bool HealthyReadSessionClosed);

/// <summary>Identifies one closed native completion milestone printed for the owning Aspire resource.</summary>
/// <param name="ProfileId">The exact admitted scale profile.</param>
/// <param name="Scenario">The actual existing scenario.</param>
/// <param name="Rate">The offered arrival rate.</param>
/// <param name="Completed">Actual terminal native calls observed.</param>
/// <param name="Started">Native calls admitted to target sessions.</param>
/// <param name="Planned">The fixed scheduled positions.</param>
public sealed record OpenLoopNativeCompletionV1(string ProfileId, Scenario Scenario, int Rate,
    long Completed, long Started, long Planned);

/// <summary>Names and bounds the separate child-process cancellation proof artifacts.</summary>
public static class OpenLoopCancellationProofContract
{
    /// <summary>Gets the fixed request path name beneath the owned output directory.</summary>
    public const string RequestFileName = "open-loop-cancel.v1.request";
    /// <summary>Gets the fixed pending request path name beneath the owned output directory.</summary>
    public const string PendingRequestFileName = ".open-loop-cancel.v1.pending";
    /// <summary>Gets the fixed separate proof JSON name beneath the owned output directory.</summary>
    public const string ProofFileName = "open-loop-cancellation-proof.v1.json";
    /// <summary>Gets the fixed child milestone marker prefix.</summary>
    public const string CompletionMarkerPrefix = "OpenLoopNativeCompletionV1";
    /// <summary>Gets the fixed request token bytes.</summary>
    public const string RequestText = "cancel-v1\n";
    /// <summary>Gets the maximum proof artifact size in bytes.</summary>
    public const int MaximumProofBytes = 4 * 1024 * 1024;
    /// <summary>Gets the maximum child marker length in ASCII bytes.</summary>
    public const int MaximumMarkerBytes = 192;
}
