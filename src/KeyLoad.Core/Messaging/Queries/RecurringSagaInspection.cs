using System.Collections.Immutable;
using System.Text;
using KeyLoad.Core.Authorization;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes RecurringSagaInspection operations against the caller's database context.</summary>
public static class RecurringSagaInspection
{
    internal const int RecurringSagaInspectionEmptyElementCount = 0;
    internal const string PayloadRedactionPrefix = "payload:";
    internal const string HeaderRedactionPrefix = "headers:";
    internal const string StateRedactionPrefix = "state:";
    /// <summary>Reads an authorized schedule projection at one bounded ZoneTree cut.</summary>

    /// <param name = "engine">The shared database execution context.</param>
    /// <param name = "principalId">The principal Id.</param>
    /// <param name = "lane">The lane.</param>
    /// <param name = "scheduleId">The schedule Id.</param>
    /// <param name = "cancellationToken">The cancellation Token.</param>
    public static RecurringScheduleInspection? InspectRecurringSchedule(this DatabaseEngine engine, string principalId, QueueLaneRef lane, Guid scheduleId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var budget = new ReadExecutionBudget(engine.Clock, cancellationToken);
        return engine.InspectRecurringScheduleRead(principalId, lane, scheduleId, budget);
    }

    internal static RecurringScheduleInspection? InspectRecurringScheduleRead(this DatabaseEngine engine, string principalId, QueueLaneRef lane, Guid scheduleId, ReadExecutionBudget budget)
    {
        var request = new InspectRecurringScheduleRequest(lane, scheduleId);
        ChargeRecurringRequest(budget, principalId, request);
        return engine.Store.Read(view =>
        {
            var read = budget.CreateView(view);
            var principal = engine.Principal(read, principalId, engine.Clock.GetUtcNow());
            engine.RequireQueueInspector(principal, lane);
            var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(read, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
            var record = read.GetRecord<RecurringScheduleRecord>(RecurringSagaStorage.ScheduleKey(lane, scheduleId));
            var result = record is null ? null : engine.ProjectSchedule(read, principal, resource, record, lane, scheduleId);
            budget.CheckResult(result);
            return result;
        });
    }

    /// <summary>Reads authorized saga state without exposing its retained timeout template.</summary>

    /// <param name = "engine">The shared database execution context.</param>
    /// <param name = "principalId">The principal Id.</param>
    /// <param name = "lane">The lane.</param>
    /// <param name = "sagaId">The saga Id.</param>
    /// <param name = "cancellationToken">The cancellation Token.</param>
    public static SagaInspection? InspectSaga(this DatabaseEngine engine, string principalId, QueueLaneRef lane, Guid sagaId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var budget = new ReadExecutionBudget(engine.Clock, cancellationToken);
        return engine.InspectSagaRead(principalId, lane, sagaId, budget);
    }

    internal static SagaInspection? InspectSagaRead(this DatabaseEngine engine, string principalId, QueueLaneRef lane, Guid sagaId, ReadExecutionBudget budget)
    {
        var request = new InspectSagaRequest(lane, sagaId);
        ChargeRecurringRequest(budget, principalId, request);
        return engine.Store.Read(view =>
        {
            var read = budget.CreateView(view);
            var principal = engine.Principal(read, principalId, engine.Clock.GetUtcNow());
            engine.RequireQueueInspector(principal, lane);
            var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(read, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
            var record = read.GetRecord<SagaRecord>(RecurringSagaStorage.SagaKey(lane, sagaId));
            var result = record is null ? null : engine.ProjectSaga(read, principal, resource, record, lane, sagaId);
            budget.CheckResult(result);
            return result;
        });
    }

    internal static void ChargeRecurringRequest<T>(ReadExecutionBudget budget, string principalId, T request)
    {
        budget.Check();
        JsonData.Identifier(principalId);
        switch (request)
        {
            case InspectRecurringScheduleRequest schedule:
                ValidateRecurringReadIdentity(schedule.Lane, schedule.ScheduleId);
                break;
            case InspectSagaRequest saga:
                ValidateRecurringReadIdentity(saga.Lane, saga.SagaId);
                break;
        }

        budget.ChargeBytes(checked(Encoding.UTF8.GetByteCount(principalId) + OrleansSerialization.Serialize(request).LongLength));
    }

    internal static void ValidateRecurringReadIdentity(QueueLaneRef lane, Guid id)
    {
        ArgumentNullException.ThrowIfNull(lane);
        EngineResourceAccess.ValidatePartition(lane.Partition);
        JsonData.Identifier(lane.Queue);
        if (id == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
    }

    internal static RecurringScheduleInspection ProjectSchedule(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource, RecurringScheduleRecord record, QueueLaneRef lane, Guid scheduleId)
    {
        RecurringScheduleValidation.ValidateScheduleRecord(record, lane, scheduleId);
        _ = RecurringSagaStorage.RequireCapacity(view, lane);
        var payload = engine.Authorization.Project(principal, resource.FieldPolicies, record.Definition.PayloadJson, out var payloadOmitted);
        var headers = engine.Authorization.Project(principal, resource.HeaderPolicies, record.Definition.HeadersJson, out var headerOmitted);
        var visiblePayload = FieldRedactionMetadata.Visible(engine.Authorization, principal, resource, payloadOmitted);
        var visibleHeaders = FieldRedactionMetadata.Visible(engine.Authorization, principal, resource with { FieldPolicies = resource.HeaderPolicies }, headerOmitted);
        var redactedFields = ProjectRedactedPaths(visiblePayload, visibleHeaders);
        var definition = record.Definition with
        {
            PayloadJson = payload,
            HeadersJson = headers
        };
        return new(definition, record.Revision, record.Generation, record.NextOrdinal, record.Cancelled, payloadOmitted.Length != RecurringSagaInspectionEmptyElementCount || headerOmitted.Length != RecurringSagaInspectionEmptyElementCount, redactedFields);
    }

    internal static SagaInspection ProjectSaga(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource, SagaRecord record, QueueLaneRef lane, Guid sagaId)
    {
        SagaCommands.ValidateSagaRecord(record, lane, sagaId);
        _ = RecurringSagaStorage.RequireCapacity(view, lane);
        var state = engine.Authorization.Project(principal, resource.FieldPolicies, record.StateJson, out var omitted);
        var redactedFields = FieldRedactionMetadata.Visible(engine.Authorization, principal, resource, omitted).Select(path => string.Concat(StateRedactionPrefix, path)).ToImmutableArray();
        return new(lane, sagaId, record.Revision, record.Phase, state, record.Deadline, omitted.Length != RecurringSagaInspectionEmptyElementCount, redactedFields);
    }

    internal static ImmutableArray<string> ProjectRedactedPaths(ImmutableArray<string> payload, ImmutableArray<string> headers)
    {
        var paths = ImmutableArray.CreateBuilder<string>(payload.Length + headers.Length);
        paths.AddRange(payload.Select(path => string.Concat(PayloadRedactionPrefix, path)));
        paths.AddRange(headers.Select(path => string.Concat(HeaderRedactionPrefix, path)));
        return paths.MoveToImmutable();
    }
}
