using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidEventVectorRole = "The native event vector source role is invalid.";

    private OperationResult ExecuteEventVectorSource(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, long position, AtomicPartitionPlacementResolution? placement)
    {
        var current = Principal(transaction, principal.Id, EvaluationClock.GetUtcNow());
        var command = Payload<EventVectorSourceMutation>(operation);
        Authorization.Require(current, command.Source.Partition, command.Source.Resource, Capability.SubscriptionsManage);
        Authorization.Require(current, command.Source.Partition, command.Source.Resource, SourceReadCapability(command.Source));
        return command.Role switch
        {
            EventVectorSourcePhaseRole.Pin or EventVectorSourcePhaseRole.Advance
                => ExecuteEventVectorSourcePin(transaction, current, operation, position, placement),
            EventVectorSourcePhaseRole.Release
                => ExecuteEventVectorSourceRelease(transaction, current, operation, position, placement),
            _ => throw Errors.Fail(ErrorCode.Validation, InvalidEventVectorRole)
        };
    }
}
