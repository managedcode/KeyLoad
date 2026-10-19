using System.Collections.Immutable;
using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Actual execution operation identity/result history, with successful receive bodies omitted.</summary>
/// <param name="Receive">Original receive, if this was a receive observation.</param>
/// <param name="Command">Original delivery command, if this was a renewal or ACK.</param>
/// <param name="ReceiveToken">Actual committed receive cut, if available.</param>
/// <param name="Claims">Actual bounded identities; this is an explicit projection, not a full ReceiveResult.</param>
/// <param name="Completion">Actual complete typed renewal/ACK result, if available.</param>
/// <param name="ReceiveFailure">Actual typed execution receive error, if available.</param>
/// <param name="ReturnedReceiveId">Actual returned correlation identity, retained even if it mismatches.</param>
/// <param name="InvocationFailure">Original invocation exception, without a manufactured execution result.</param>
/// <param name="Stage">Prepared before execution invocation, actually returned, or originally threw.</param>
public sealed record QueueWorkerOperationObservation(ReceiveRequest? Receive, DeliveryCommand? Command,
    CommitToken? ReceiveToken, ImmutableArray<QueueWorkerClaimIdentity> Claims,
    Result<CommitReceipt>? Completion, Problem? ReceiveFailure, Guid? ReturnedReceiveId = null, Exception? InvocationFailure = null, QueueWorkerOperationStage Stage = QueueWorkerOperationStage.Returned);
