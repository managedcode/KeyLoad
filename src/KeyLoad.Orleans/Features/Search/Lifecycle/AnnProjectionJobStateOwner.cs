using KeyLoad.Core;
using Microsoft.Extensions.Options;
using Orleans.Journaling;

namespace KeyLoad.Orleans;

#pragma warning disable ORLEANSEXP005 // ADR-110: native journal-backed ANN durable state only.
internal sealed class AnnProjectionJobStateOwner(IDurableStateManager manager,
    IOptions<RuntimeJournalOptions> journal, int maximumBatchBytes)
{
    private readonly RuntimeJournalOptions options = journal.Value;
    private readonly IDurableValue<AnnProjectionJobState> value =
        manager.GetOrAddValue<AnnProjectionJobState>(AnnProjectionJobProtocol.StateName);
    internal bool Open { get; private set; } = true;
    internal AnnProjectionJobState? Current => Validate(value.Value);

    internal async Task WriteAsync(AnnProjectionJobState next, CancellationToken token)
    {
        RequireOpen();
        _ = Validate(next);
        var size = NativeSerialization.Measure(next);
        if (size > options.MaximumJournalBytes || size > maximumBatchBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProjectionJobProtocol.Exhausted); }
        value.Value = next;
        try
        { await manager.WriteStateAsync(token).ConfigureAwait(true); }
        catch (Exception) { Open = false; throw; }
    }

    private AnnProjectionJobState? Validate(AnnProjectionJobState? current)
    {
        if (current is null) { return null; }
        if (current.Authority is not { } authority || current.InitialRequest is not { } initial
            || string.IsNullOrWhiteSpace(authority.PrincipalId) || authority.ConfigureCommandId == Guid.Empty
            || authority.PolicyEpoch <= EmptySequence || authority.Incarnation == Guid.Empty
            || initial.Consumer is null || initial.Placement is null
            || initial.CommandId == Guid.Empty || initial.Mode != AnnMaintenanceMode.Build
            || initial.MaintenanceProfile is null || current.RequiredThroughSequence < EmptySequence
            || current.PendingRequest is null && current.LastRequest is null
            || (current.LastRequest is null) != (current.LastResult is null)
            || current.JobId is { Length: EmptyJobNameLength }
            || current.JobId is { } job && System.Text.Encoding.UTF8.GetByteCount(job) > options.MaximumNameBytes
            || current.BlockedError is { } blocked && blocked is not (ErrorCode.PermissionDenied
                or ErrorCode.Unauthenticated or ErrorCode.TokenInvalidated))
        { throw Errors.Fail(ErrorCode.Corruption, AnnProjectionJobProtocol.Invalid); }
        RequireRequest(initial, current.PendingRequest);
        RequireRequest(initial, current.LastRequest);
        if (current.LastResult is { } result && current.LastRequest is { } last
            && (result.CommandId != last.CommandId || result.Consumer != last.Consumer
                || result.IndexGeneration != last.IndexGeneration || result.Phase != AnnMaintenancePhase.Completed
                || result.Count < EmptySequence || result.Source is not { } source
                || source.NodeId != last.NodeId || source.Incarnation != authority.Incarnation
                || source.PolicyEpoch != authority.PolicyEpoch || result.IndexSha256 is not { Length: DigestCharacters }))
        { throw Errors.Fail(ErrorCode.Corruption, AnnProjectionJobProtocol.Invalid); }
        var bytes = NativeSerialization.Measure(current);
        if (bytes > options.MaximumJournalBytes || bytes > maximumBatchBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProjectionJobProtocol.Exhausted); }
        return current;
    }

    private static void RequireRequest(AnnMaintenanceRequest initial, AnnMaintenanceRequest? request)
    {
        if (request is null) { return; }
        if (request.CommandId == Guid.Empty || request.Consumer != initial.Consumer
            || request.IndexGeneration != initial.IndexGeneration || request.NodeId != initial.NodeId
            || request.Placement is not { } placement
            || placement.PhysicalShardId != initial.Placement.PhysicalShardId
            || placement.Incarnation != initial.Placement.Incarnation
            || placement.PlacementEpoch != initial.Placement.PlacementEpoch
            || !placement.VoterIds.SequenceEqual(initial.Placement.VoterIds, StringComparer.Ordinal)
            || request.MaintenanceProfile != initial.MaintenanceProfile
            || request.Mode is not (AnnMaintenanceMode.Build or AnnMaintenanceMode.Restore))
        { throw Errors.Fail(ErrorCode.Corruption, AnnProjectionJobProtocol.Invalid); }
    }
    private const long EmptySequence = 0;
    private const int EmptyJobNameLength = 0;
    private const int DigestCharacters = 64;

    internal void RequireOpen()
    {
        if (!Open)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProjectionJobProtocol.Unresolved); }
    }
}
#pragma warning restore ORLEANSEXP005
