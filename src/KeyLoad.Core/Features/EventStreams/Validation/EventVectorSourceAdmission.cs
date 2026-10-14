using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private AtomicPartitionPlacementResolution AuthorizeEventVectorSource(IKeyValueView view,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        var current = Principal(view, principal.Id, EvaluationClock.GetUtcNow());
        var command = Payload<EventVectorSourceMutation>(operation);
        if (command.Source?.Partition is null || command.SourceOwner is null || command.ControlOwner is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, MissingEventVectorSourceAuthority);
        }
        Authorization.Require(current, command.Source.Partition, command.Source.Resource, Capability.SubscriptionsManage);
        Authorization.Require(current, command.Source.Partition, command.Source.Resource, SourceReadCapability(command.Source));
        var placement = StreamTraversalPlacement(view, command.Source.Partition, command.SourceOwner);
        RequireEventVectorSourceApply(view, current, command, placement);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, MissingEventVectorSourceAuthority);
        if (!PhysicalOwnerEntryValidation.SameOwner(command.ControlOwner, catalog.DefaultShard)
            || command.ControlIncarnation != command.ControlOwner.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, MissingEventVectorSourceAuthority);
        }
        if (SourceResource(view, command.Source).SchemaVersion != command.SourceSchemaVersion)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, MissingEventVectorSourceAuthority);
        }
        return placement;
    }
}
