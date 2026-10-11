using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteEventVectorSourceRelease(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, long position,
        AtomicPartitionPlacementResolution? placement)
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
        var retained = ReadEventVectorPinRow(transaction, request.Source, request.MapId, admission);
        var original = retained is { } row
            ? NativeSerialization.Deserialize<EventVectorSourcePin>(row.Value.Span) : null;
        var released = EventVectorReleaseTransition.Prepare(request, original, admission);
        var observed = EventVectorOriginalPinOutcome.Read(transaction, request, admission);
        var settled = observed.Outcome is not null
            || original is { Released: true } && original.OriginalPinExpiresAt <= EvaluationClock.GetUtcNow();
        if (settled)
        {
            if (retained is { } existing)
            { EventVectorSourcePinWriter.Delete(transaction, request.Source, request.MapId, existing, admission); }
        }
        else
        { EventVectorSourcePinWriter.Write(transaction, retained, released, admission); }
        var token = new CommitToken(placement!.Incarnation, request.Source.Partition.AtomicPartitionId,
            position, placement.PlacementEpoch);
        return Result(new EventVectorSourceReleaseResult
        {
            Version = EventVectorSourceResultVersion,
            CommandId = operation.Id,
            Source = request.Source,
            OriginalPinCommandId = request.OriginalPinCommandId,
            Disposition = settled ? EventVectorReleaseDisposition.Released : EventVectorReleaseDisposition.Retained,
            RetainedPin = settled ? null : released,
            Token = token,
            Durability = Durability,
            OriginalOutcomeDigest = observed.Digest
        });
    }
}
