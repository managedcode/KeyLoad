using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EventVectorSourceResultVersion = 1;
    private const string EventVectorApplyAuthorityChanged = "The original event vector source apply authority changed.";

    private OperationResult ExecuteEventVectorSourcePin(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, long position, AtomicPartitionPlacementResolution? placement)
    {
        var currentPrincipal = Principal(transaction, principal.Id, EvaluationClock.GetUtcNow());
        var request = Payload<EventVectorSourceMutation>(operation);
        Authorization.Require(currentPrincipal, request.Source.Partition, request.Source.Resource, Capability.SubscriptionsManage);
        Authorization.Require(currentPrincipal, request.Source.Partition, request.Source.Resource, SourceReadCapability(request.Source));
        RequireEventVectorSourceApply(transaction, currentPrincipal, request, placement);
        var resource = SourceResource(transaction, request.Source);
        if (resource.SchemaVersion != request.SourceSchemaVersion)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, EventVectorApplyAuthorityChanged); }
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        var head = EventVectorStoredSourceHead(transaction, request.Source);
        var retained = ReadEventVectorPinRow(transaction, request.Source, request.MapId, admission);
        var original = retained is { } row
            ? NativeSerialization.Deserialize<EventVectorSourcePin>(row.Value.Span) : null;
        var body = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span).Value;
        var changed = EventVectorPinTransition.Prepare(request, operation.Id, body, head, original, admission);
        EventVectorSourcePinWriter.Write(transaction, retained, changed, admission);
        var token = new CommitToken(placement!.Incarnation, request.Source.Partition.AtomicPartitionId,
            position, placement.PlacementEpoch);
        return Result(new EventVectorSourceResult
        {
            Version = EventVectorSourceResultVersion,
            CommandId = operation.Id,
            Pin = changed,
            Token = token,
            Durability = Durability
        });
    }

    private void RequireEventVectorSourceApply(IKeyValueView view, PrincipalRecord principal,
        EventVectorSourceMutation request, AtomicPartitionPlacementResolution? placement)
    {
        var authority = StreamTraversalPlacement(view, request.Source.Partition, request.SourceOwner);
        if (placement is null || placement.Partition != request.Source.Partition
            || placement.PhysicalShardId != request.SourceOwner.PhysicalShardId
            || !placement.VoterIds.SequenceEqual(request.SourceOwner.VoterIds, StringComparer.Ordinal)
            || placement.Incarnation != request.SourceOwner.Incarnation
            || placement.PlacementEpoch != request.SourceOwner.PlacementEpoch
            || authority.Revision != request.LogicalPlacementRevision
            || authority.DirectoryRevision != request.DirectoryFence
            || principal.Id != request.PrincipalId || principal.PolicyEpoch != request.SourcePolicyEpoch
            || request.FirstExpiresAt <= EvaluationClock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorApplyAuthorityChanged); }
    }

    private static EventVectorEncodedRow? ReadEventVectorPinRow(IKeyValueView view,
        EventSourceRef source, Guid mapId, EventVectorAdmissionPolicy admission)
    {
        var key = EventVectorKeys.Pin(source, mapId);
        EventVectorEncodedRow? result = null;
        view.ReadValue(key, value =>
        {
            admission.RequireEncodedBytes(checked(key.LongLength + value.Length));
            result = new EventVectorEncodedRow(key, value.ToArray());
        });
        return result;
    }
}
