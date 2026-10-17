using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string MissingEventVectorSourceAuthority = "The original event vector source authority changed.";

    internal ReplicatedOperation CreateVerifiedEventVectorSourcePhase(string localPrincipalId,
        EventVectorSourcePhase verifiedPhase, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(verifiedPhase);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        if (verifiedPhase.Source is null || verifiedPhase.SourceOwner is null || verifiedPhase.ControlOwner is null)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, MissingEventVectorSourceAuthority);
        }
        var now = EvaluationClock.GetUtcNow();
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        admission.RequireEncodedBytes(NativeSerialization.Measure(verifiedPhase));
        var mutation = Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var principal = Principal(bounded, localPrincipalId, now);
            Authorization.Require(principal, verifiedPhase.Source.Partition, verifiedPhase.Source.Resource,
                Capability.SubscriptionsManage);
            Authorization.Require(principal, verifiedPhase.Source.Partition, verifiedPhase.Source.Resource,
                SourceReadCapability(verifiedPhase.Source));
            var command = EventVectorSourceMutationValidation.ReadOriginal(verifiedPhase, admission);
            var placement = StreamTraversalPlacement(bounded, command.Source.Partition, command.SourceOwner);
            RequireEventVectorSourceApply(bounded, principal, command, placement);
            var catalog = PhysicalShardCatalogRecordSerialization.Read(bounded)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, MissingEventVectorSourceAuthority);
            if (!PhysicalOwnerEntryValidation.SameOwner(command.ControlOwner, catalog.DefaultShard)
                || command.ControlIncarnation != command.ControlOwner.Incarnation)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, MissingEventVectorSourceAuthority);
            }
            var resource = SourceResource(bounded, command.Source);
            if (resource.SchemaVersion != command.SourceSchemaVersion)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, MissingEventVectorSourceAuthority);
            }
            if (command.Role != EventVectorSourcePhaseRole.Release)
            {
                _ = EventVectorStoredSourceHead(bounded, command.Source);
            }
            return command;
        });
        work.Check();
        var originalBody = verifiedPhase.OriginalNativeBody.ToArray();
        var identity = Identity<EventVectorSourceMutation>(originalBody);
        RequireNativeBudget(System.Text.Encoding.UTF8.GetByteCount(identity));
        var operation = IssueNativeOperation(new(verifiedPhase.PhaseCommandId,
            OperationKind.EventFeedSourcePhase, mutation.PrincipalId, now, identity), new NativeCommandPayload(originalBody));
        work.Check();
        return operation;
    }
}
