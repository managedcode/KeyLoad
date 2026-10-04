using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal void AuthorizeRecurringSagaRequest(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, Mutation mutation)
    {
        switch (mutation)
        {
            case ConfigureRecurringSchedule configure:
                var definition = NormalizeScheduleDefinition(configure.Definition);
                ValidateScheduleScope(definition.Lane, definition.ScheduleId, partition);
                RequireScheduleWriteAuthority(principal, definition.Lane, Resource(view, partition,
                    definition.Lane.Queue, ResourceKind.WorkQueue));
                break;
            case EmitRecurringOccurrences emit:
                ValidateScheduleScope(emit.Lane, emit.ScheduleId, partition);
                RequireScheduleWriteAuthority(principal, emit.Lane, Resource(view, partition,
                    emit.Lane.Queue, ResourceKind.WorkQueue));
                break;
            case CancelRecurringSchedule cancel:
                ValidateScheduleScope(cancel.Lane, cancel.ScheduleId, partition);
                RequireScheduleCapability(principal, cancel.Lane);
                break;
            case CompareExchangeSaga saga:
                _ = AuthorizeSagaWrite(view, principal, saga, partition);
                break;
            case ExpireSaga expire:
                ValidateScheduleScope(expire.Lane, expire.SagaId, partition);
                RequireScheduleCapability(principal, expire.Lane);
                _ = Resource(view, partition, expire.Lane.Queue, ResourceKind.WorkQueue);
                var sagaRecord = RequireSaga(view, expire.Lane, expire.SagaId);
                if (sagaRecord.Timeout is { } timeout)
                {
                    var destination = Resource(view, partition, timeout.Queue.Queue, ResourceKind.WorkQueue);
                    RequireScheduleWriteAuthority(principal, timeout.Queue, destination);
                }
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
    }

    internal void ReauthorizeRecurringSagaEffect(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, Mutation mutation)
    {
        AuthorizeRecurringSagaRequest(view, principal, partition, mutation);
        switch (mutation)
        {
            case ConfigureRecurringSchedule configure:
                RequireScheduleCreatorWrite(view, configure.Definition.Lane, configure.Definition.ScheduleId,
                    Clock.GetUtcNow());
                break;
            case EmitRecurringOccurrences emit:
                RequireScheduleCreator(view, emit.Lane, emit.ScheduleId, Clock.GetUtcNow());
                break;
            case CancelRecurringSchedule cancel:
                RequireScheduleCreatorManagement(view, cancel.Lane, cancel.ScheduleId, Clock.GetUtcNow());
                break;
            case CompareExchangeSaga saga:
                RequireSagaCreatorWrite(view, saga.Lane, saga.SagaId, saga.Timeout, Clock.GetUtcNow());
                break;
            case ExpireSaga expire:
                RequireSagaCreator(view, expire.Lane, expire.SagaId, Clock.GetUtcNow(), true);
                break;
        }
    }

    private void RequireScheduleWriteAuthority(PrincipalRecord principal, QueueLaneRef lane,
        ResourceDefinition resource)
    {
        RequireScheduleCapability(principal, lane);
        RequireQueueFieldWrites(principal, resource);
    }

    private void RequireScheduleCapability(PrincipalRecord principal, QueueLaneRef lane)
    {
        var dataPrincipal = principal with { ClusterAdministrator = false };
        Authorization.Require(dataPrincipal, lane.Partition, lane.Queue,
            Capability.SchedulerManage | Capability.QueuePublish);
    }

    private void RequireQueueInspector(PrincipalRecord principal, QueueLaneRef lane)
    {
        var dataPrincipal = principal with { ClusterAdministrator = false };
        Authorization.Require(dataPrincipal, lane.Partition, lane.Queue, Capability.QueueInspect);
    }

    private void RequireQueueFieldWrites(PrincipalRecord principal, ResourceDefinition resource)
    {
        var dataPrincipal = principal with { ClusterAdministrator = false };
        foreach (var policy in resource.FieldPolicies)
        {
            Authorization.RequireFieldWrite(dataPrincipal, resource, policy.Path);
        }
        foreach (var policy in resource.HeaderPolicies)
        {
            Authorization.RequireFieldWrite(dataPrincipal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }
    }

    private void RequireQueueFieldUses(PrincipalRecord principal, ResourceDefinition resource)
    {
        var dataPrincipal = principal with { ClusterAdministrator = false };
        foreach (var policy in resource.FieldPolicies)
        {
            Authorization.RequireFieldUse(dataPrincipal, resource, policy.Path);
        }
        foreach (var policy in resource.HeaderPolicies)
        {
            Authorization.RequireFieldUse(dataPrincipal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }
    }

    private void RequireScheduleCreator(IKeyValueView view, QueueLaneRef lane, Guid scheduleId, DateTimeOffset now)
    {
        var record = RequireSchedule(view, lane, scheduleId);
        var creator = Principal(view, record.CreatorPrincipalId, now);
        var resource = Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        RequireScheduleCapability(creator, lane);
        RequireQueueFieldUses(creator, resource);
    }

    private void RequireScheduleCreatorManagement(IKeyValueView view, RecurringScheduleRecord record,
        DateTimeOffset now)
    {
        var creator = Principal(view, record.CreatorPrincipalId, now);
        RequireScheduleCapability(creator, record.Lane);
    }

    private void RequireScheduleCreatorManagement(IKeyValueView view, QueueLaneRef lane, Guid scheduleId,
        DateTimeOffset now)
        => RequireScheduleCreatorManagement(view, RequireSchedule(view, lane, scheduleId), now);

    private void RequireScheduleCreatorWrite(IKeyValueView view, QueueLaneRef lane, Guid scheduleId,
        DateTimeOffset now)
    {
        var record = RequireSchedule(view, lane, scheduleId);
        var creator = Principal(view, record.CreatorPrincipalId, now);
        var resource = Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        RequireScheduleCapability(creator, lane);
        RequireQueueFieldWrites(creator, resource);
    }

    private void RequireScheduleCreatorWrite(IKeyValueView view, RecurringScheduleRecord record,
        ResourceDefinition resource, DateTimeOffset now)
    {
        var creator = Principal(view, record.CreatorPrincipalId, now);
        RequireScheduleCapability(creator, record.Lane);
        RequireQueueFieldWrites(creator, resource);
    }

    private void RequireSagaCreatorWrite(IKeyValueView view, QueueLaneRef lane, Guid sagaId,
        SagaTimeoutDefinition? timeout, DateTimeOffset now)
        => RequireSagaCreatorWrite(view, RequireSaga(view, lane, sagaId), timeout, now);

    private void RequireSagaCreatorWrite(IKeyValueView view, SagaRecord record,
        SagaTimeoutDefinition? timeout, DateTimeOffset now)
    {
        var creator = Principal(view, record.CreatorPrincipalId, now);
        var lane = record.Lane;
        var source = Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        RequireScheduleCapability(creator, lane);
        RequireQueueFieldWrites(creator, source);
        if (timeout is not null)
        {
            var destination = Resource(view, lane.Partition, timeout.Queue.Queue, ResourceKind.WorkQueue);
            RequireScheduleWriteAuthority(creator, timeout.Queue, destination);
        }
    }

    private void RequireSagaCreator(IKeyValueView view, QueueLaneRef lane, Guid sagaId, DateTimeOffset now,
        bool timeout)
    {
        var record = RequireSaga(view, lane, sagaId);
        var creator = Principal(view, record.CreatorPrincipalId, now);
        var source = Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        RequireScheduleCapability(creator, lane);
        RequireQueueFieldUses(creator, source);
        if (timeout && record.Timeout is { } definition)
        {
            var destination = Resource(view, lane.Partition, definition.Queue.Queue, ResourceKind.WorkQueue);
            RequireScheduleWriteAuthority(creator, definition.Queue, destination);
        }
    }
}
