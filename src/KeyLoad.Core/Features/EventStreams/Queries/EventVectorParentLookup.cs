using System.Security.Cryptography;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorParentLookup
{
    private const int Version = 1;
    private const string InvalidLookup = "The original event vector parent outcome requires recovery.";

    internal static StoredOutcome? ReadTerminal(IKeyValueView view, IAuthorizationPolicy authorization, PrincipalRecord principal,
        EventFeedControlRequest request, Guid incarnation, IOptions<DatabaseLimits> options,
        CancellationToken cancellationToken)
        => ReadTerminalDetails(view, authorization, principal, request, incarnation, options, cancellationToken)?.Outcome;

    internal static (StoredOutcome Outcome, ReadOnlyMemory<byte> PhaseBytes, Guid PhaseId)? ReadTerminalDetails(
        IKeyValueView view, IAuthorizationPolicy authorization, PrincipalRecord principal, EventFeedControlRequest request, Guid incarnation,
        IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var budget = new EventVectorInventoryReadBudget(options);
        var key = EventVectorKeys.ParentOutcome(request.ControlPartition, principal.Id, request.CommandId);
        EventVectorParentOutcomeIndex? index = null;
        cancellationToken.ThrowIfCancellationRequested();
        view.ReadValue(key, bytes =>
        {
            admission.RequireEncodedBytes(checked(key.Length + bytes.Length));
            index = NativeSerialization.Deserialize<EventVectorParentOutcomeIndex>(bytes);
        }, budget.Observe);
        if (index is null)
        { return null; }
        RequireIndex(index, principal, request, admission);
        EventVectorParentScopes.Authorize(authorization, principal, index.OriginalAuthorizedSourceScopes, admission);
        cancellationToken.ThrowIfCancellationRequested();
        admission.RequireEncodedBytes(index.OriginalNativeTerminalPhaseBytes.Length);
        var phase = NativeSerialization.Deserialize<EventVectorControlPhase>(index.OriginalNativeTerminalPhaseBytes.Span);
        if (!CryptographicOperations.FixedTimeEquals(
            EventVectorControlBodyProjection.RequestDigest(phase.OriginalRequest, admission).Span,
            index.OriginalPublicRequestDigest.Span))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidLookup); }
        EventVectorParentScopes.RequireExact(index.OriginalAuthorizedSourceScopes,
            EventVectorParentScopes.Derive(phase, null, admission));
        var outcome = EventVectorParentOutcome.Require(view, principal, phase, index.ActualTerminalControlPhaseId,
            index.ActualTerminalOutcomeFingerprint, incarnation, admission);
        return (outcome, index.OriginalNativeTerminalPhaseBytes, index.ActualTerminalControlPhaseId);
    }

    private static void RequireIndex(EventVectorParentOutcomeIndex index, PrincipalRecord principal,
        EventFeedControlRequest request, EventVectorAdmissionPolicy admission)
    {
        if (index.Version != Version || index.PrincipalId != principal.Id
            || index.ControlPartition != request.ControlPartition
            || index.OriginalPublicCommandId != request.CommandId || index.ActualTerminalControlPhaseId == Guid.Empty
            || index.OriginalNativeTerminalPhaseBytes.IsEmpty || string.IsNullOrWhiteSpace(index.ActualTerminalOutcomeFingerprint)
            || index.OriginalAuthorizedSourceScopes.IsDefault
            || index.OriginalPublicRequestDigest.Length != SHA256.HashSizeInBytes)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidLookup); }
        if (index.MapId != request.MapId || !CryptographicOperations.FixedTimeEquals(
            EventVectorControlBodyProjection.RequestDigest(request, admission).Span,
            index.OriginalPublicRequestDigest.Span))
        { throw Errors.Fail(ErrorCode.Conflict, InvalidLookup); }
    }
}
