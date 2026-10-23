using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes RecurringSagaAuthorization operations against the caller's database context.</summary>
public static class RecurringSagaAuthorization
{
    internal static void RequireRecurringSagaCapabilities(this DatabaseEngine engine, PrincipalRecord principal, Mutation mutation)
    {
        switch (mutation)
        {
            case ConfigureRecurringSchedule configure:
                engine.RequireScheduleCapability(principal, configure.Definition.Lane);
                break;
            case EmitRecurringOccurrences emit:
                engine.RequireScheduleCapability(principal, emit.Lane);
                break;
            case CancelRecurringSchedule cancel:
                engine.RequireScheduleCapability(principal, cancel.Lane);
                break;
            case CompareExchangeSaga saga:
                engine.RequireScheduleCapability(principal, saga.Lane);
                break;
            case ExpireSaga expire:
                engine.RequireScheduleCapability(principal, expire.Lane);
                break;
        }
    }

    internal static void AuthorizeRecurringSagaRequest(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        switch (mutation)
        {
            case ConfigureRecurringSchedule configure:
                var definition = engine.NormalizeScheduleDefinition(configure.Definition);
                RecurringScheduleValidation.ValidateScheduleScope(definition.Lane, definition.ScheduleId, partition);
                engine.RequireScheduleCapability(principal, definition.Lane);
                engine.RequireScheduleWriteAuthority(principal, definition.Lane, engine.Resource(view, partition, definition.Lane.Queue, ResourceKind.WorkQueue));
                break;
            case EmitRecurringOccurrences emit:
                RecurringScheduleValidation.ValidateScheduleScope(emit.Lane, emit.ScheduleId, partition);
                engine.RequireScheduleCapability(principal, emit.Lane);
                engine.RequireScheduleWriteAuthority(principal, emit.Lane, engine.Resource(view, partition, emit.Lane.Queue, ResourceKind.WorkQueue));
                break;
            case CancelRecurringSchedule cancel:
                RecurringScheduleValidation.ValidateScheduleScope(cancel.Lane, cancel.ScheduleId, partition);
                engine.RequireScheduleCapability(principal, cancel.Lane);
                break;
            case CompareExchangeSaga saga:
                _ = engine.AuthorizeSagaWrite(view, principal, saga, partition);
                break;
            case ExpireSaga expire:
                RecurringScheduleValidation.ValidateScheduleScope(expire.Lane, expire.SagaId, partition);
                engine.RequireScheduleCapability(principal, expire.Lane);
                _ = engine.Resource(view, partition, expire.Lane.Queue, ResourceKind.WorkQueue);
                var sagaRecord = engine.RequireSaga(view, expire.Lane, expire.SagaId);
                if (sagaRecord.Timeout is { } timeout)
                {
                    engine.RequireScheduleCapability(principal, timeout.Queue);
                    var destination = engine.Resource(view, partition, timeout.Queue.Queue, ResourceKind.WorkQueue);
                    engine.RequireScheduleWriteAuthority(principal, timeout.Queue, destination);
                }

                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
    }

    internal static void ReauthorizeRecurringSagaEffect(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        engine.AuthorizeRecurringSagaRequest(view, principal, partition, mutation);
        switch (mutation)
        {
            case ConfigureRecurringSchedule configure:
                engine.RequireScheduleCreatorWrite(view, configure.Definition.Lane, configure.Definition.ScheduleId, engine.Clock.GetUtcNow());
                break;
            case EmitRecurringOccurrences emit:
                engine.RequireScheduleCreator(view, emit.Lane, emit.ScheduleId, engine.Clock.GetUtcNow());
                break;
            case CancelRecurringSchedule cancel:
                engine.RequireScheduleCreatorManagement(view, cancel.Lane, cancel.ScheduleId, engine.Clock.GetUtcNow());
                break;
            case CompareExchangeSaga saga:
                engine.RequireSagaCreatorWrite(view, saga.Lane, saga.SagaId, saga.Timeout, engine.Clock.GetUtcNow());
                break;
            case ExpireSaga expire:
                engine.RequireSagaCreator(view, expire.Lane, expire.SagaId, engine.Clock.GetUtcNow(), true);
                break;
        }
    }

    internal static void RequireScheduleWriteAuthority(this DatabaseEngine engine, PrincipalRecord principal, QueueLaneRef lane, ResourceDefinition resource)
    {
        engine.RequireScheduleCapability(principal, lane);
        engine.RequireQueueFieldWrites(principal, resource);
    }

    internal static void RequireScheduleCapability(this DatabaseEngine engine, PrincipalRecord principal, QueueLaneRef lane)
    {
        var dataPrincipal = principal with
        {
            ClusterAdministrator = false
        };
        engine.Authorization.Require(dataPrincipal, lane.Partition, lane.Queue, Capability.SchedulerManage | Capability.QueuePublish);
    }

    internal static void RequireQueueInspector(this DatabaseEngine engine, PrincipalRecord principal, QueueLaneRef lane)
    {
        var dataPrincipal = principal with
        {
            ClusterAdministrator = false
        };
        engine.Authorization.Require(dataPrincipal, lane.Partition, lane.Queue, Capability.QueueInspect);
    }

    internal static void RequireQueueFieldWrites(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource)
    {
        var dataPrincipal = principal with
        {
            ClusterAdministrator = false
        };
        foreach (var policy in resource.FieldPolicies)
        {
            engine.Authorization.RequireFieldWrite(dataPrincipal, resource, policy.Path);
        }

        foreach (var policy in resource.HeaderPolicies)
        {
            engine.Authorization.RequireFieldWrite(dataPrincipal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }
    }

    internal static void RequireQueueFieldUses(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource)
    {
        var dataPrincipal = principal with
        {
            ClusterAdministrator = false
        };
        foreach (var policy in resource.FieldPolicies)
        {
            engine.Authorization.RequireFieldUse(dataPrincipal, resource, policy.Path);
        }

        foreach (var policy in resource.HeaderPolicies)
        {
            engine.Authorization.RequireFieldUse(dataPrincipal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }
    }

    internal static void RequireScheduleCreator(this DatabaseEngine engine, IKeyValueView view, QueueLaneRef lane, Guid scheduleId, DateTimeOffset now)
    {
        var record = engine.RequireSchedule(view, lane, scheduleId);
        var creator = engine.Principal(view, record.CreatorPrincipalId, now);
        engine.RequireScheduleCapability(creator, lane);
        var resource = engine.Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        engine.RequireQueueFieldUses(creator, resource);
    }

    internal static void RequireScheduleCreatorManagement(this DatabaseEngine engine, IKeyValueView view, RecurringScheduleRecord record, DateTimeOffset now)
    {
        var creator = engine.Principal(view, record.CreatorPrincipalId, now);
        engine.RequireScheduleCapability(creator, record.Lane);
    }

    internal static void RequireScheduleCreatorManagement(this DatabaseEngine engine, IKeyValueView view, QueueLaneRef lane, Guid scheduleId, DateTimeOffset now) => engine.RequireScheduleCreatorManagement(view, engine.RequireSchedule(view, lane, scheduleId), now);
    internal static void RequireScheduleCreatorWrite(this DatabaseEngine engine, IKeyValueView view, QueueLaneRef lane, Guid scheduleId, DateTimeOffset now)
    {
        var record = engine.RequireSchedule(view, lane, scheduleId);
        var creator = engine.Principal(view, record.CreatorPrincipalId, now);
        engine.RequireScheduleCapability(creator, lane);
        var resource = engine.Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        engine.RequireQueueFieldWrites(creator, resource);
    }

    internal static void RequireScheduleCreatorWrite(this DatabaseEngine engine, IKeyValueView view, RecurringScheduleRecord record, ResourceDefinition resource, DateTimeOffset now)
    {
        var creator = engine.Principal(view, record.CreatorPrincipalId, now);
        engine.RequireScheduleCapability(creator, record.Lane);
        engine.RequireQueueFieldWrites(creator, resource);
    }

    internal static void RequireSagaCreatorWrite(this DatabaseEngine engine, IKeyValueView view, QueueLaneRef lane, Guid sagaId, SagaTimeoutDefinition? timeout, DateTimeOffset now) => engine.RequireSagaCreatorWrite(view, engine.RequireSaga(view, lane, sagaId), timeout, now);
    internal static void RequireSagaCreatorWrite(this DatabaseEngine engine, IKeyValueView view, SagaRecord record, SagaTimeoutDefinition? timeout, DateTimeOffset now)
    {
        var creator = engine.Principal(view, record.CreatorPrincipalId, now);
        var lane = record.Lane;
        engine.RequireScheduleCapability(creator, lane);
        var source = engine.Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        engine.RequireQueueFieldWrites(creator, source);
        if (timeout is not null)
        {
            engine.RequireScheduleCapability(creator, timeout.Queue);
            var destination = engine.Resource(view, lane.Partition, timeout.Queue.Queue, ResourceKind.WorkQueue);
            engine.RequireScheduleWriteAuthority(creator, timeout.Queue, destination);
        }
    }

    internal static void RequireSagaCreator(this DatabaseEngine engine, IKeyValueView view, QueueLaneRef lane, Guid sagaId, DateTimeOffset now, bool timeout)
    {
        var record = engine.RequireSaga(view, lane, sagaId);
        var creator = engine.Principal(view, record.CreatorPrincipalId, now);
        engine.RequireScheduleCapability(creator, lane);
        var source = engine.Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        engine.RequireQueueFieldUses(creator, source);
        if (timeout && record.Timeout is { } definition)
        {
            engine.RequireScheduleCapability(creator, definition.Queue);
            var destination = engine.Resource(view, lane.Partition, definition.Queue.Queue, ResourceKind.WorkQueue);
            engine.RequireScheduleWriteAuthority(creator, definition.Queue, destination);
        }
    }
}
