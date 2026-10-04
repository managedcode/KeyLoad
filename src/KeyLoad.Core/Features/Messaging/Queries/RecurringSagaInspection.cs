using System.Collections.Immutable;
using System.Text;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string PayloadRedactionPrefix = "payload:";
    private const string HeaderRedactionPrefix = "headers:";
    private const string StateRedactionPrefix = "state:";

    /// <summary>Reads an authorized schedule projection at one bounded ZoneTree cut.</summary>
    public RecurringScheduleInspection? InspectRecurringSchedule(string principalId, QueueLaneRef lane,
        Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var budget = new ReadExecutionBudget(Limits, Clock, cancellationToken);
        var request = new InspectRecurringScheduleRequest(lane, scheduleId);
        ChargeRecurringRequest(budget, principalId, request);
        return Store.Read(view =>
        {
            var read = budget.CreateView(view);
            var principal = Principal(read, principalId, Clock.GetUtcNow());
            RequireQueueInspector(principal, lane);
            var resource = Resource(read, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
            var record = read.GetRecord<RecurringScheduleRecord>(RecurringSagaStorage.ScheduleKey(lane, scheduleId));
            var result = record is null ? null : ProjectSchedule(read, principal, resource, record, lane, scheduleId);
            budget.CheckResult(result);
            return result;
        });
    }

    /// <summary>Reads authorized saga state without exposing its retained timeout template.</summary>
    public SagaInspection? InspectSaga(string principalId, QueueLaneRef lane, Guid sagaId,
        CancellationToken cancellationToken = default)
    {
        var budget = new ReadExecutionBudget(Limits, Clock, cancellationToken);
        var request = new InspectSagaRequest(lane, sagaId);
        ChargeRecurringRequest(budget, principalId, request);
        return Store.Read(view =>
        {
            var read = budget.CreateView(view);
            var principal = Principal(read, principalId, Clock.GetUtcNow());
            RequireQueueInspector(principal, lane);
            var resource = Resource(read, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
            var record = read.GetRecord<SagaRecord>(RecurringSagaStorage.SagaKey(lane, sagaId));
            var result = record is null ? null : ProjectSaga(read, principal, resource, record, lane, sagaId);
            budget.CheckResult(result);
            return result;
        });
    }

    private void ChargeRecurringRequest<T>(ReadExecutionBudget budget, string principalId, T request)
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
        budget.ChargeBytes(checked(Encoding.UTF8.GetByteCount(principalId)
            + NativeSerialization.Serialize(request).LongLength));
    }

    private static void ValidateRecurringReadIdentity(QueueLaneRef lane, Guid id)
    {
        ArgumentNullException.ThrowIfNull(lane);
        ValidatePartition(lane.Partition);
        JsonData.Identifier(lane.Queue);
        if (id == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
    }

    private RecurringScheduleInspection ProjectSchedule(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, RecurringScheduleRecord record, QueueLaneRef lane, Guid scheduleId)
    {
        ValidateScheduleRecord(record, lane, scheduleId);
        _ = RecurringSagaStorage.RequireCapacity(view, lane);
        var payload = Authorization.Project(principal, resource.FieldPolicies, record.Definition.PayloadJson, out var payloadOmitted);
        var headers = Authorization.Project(principal, resource.HeaderPolicies, record.Definition.HeadersJson, out var headerOmitted);
        var redactedFields = ProjectRedactedPaths(payloadOmitted, headerOmitted);
        var definition = record.Definition with { PayloadJson = payload, HeadersJson = headers };
        return new(definition, record.Revision, record.Generation, record.NextOrdinal, record.Cancelled,
            redactedFields.Length != 0, redactedFields);
    }

    private SagaInspection ProjectSaga(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource,
        SagaRecord record, QueueLaneRef lane, Guid sagaId)
    {
        ValidateSagaRecord(record, lane, sagaId);
        _ = RecurringSagaStorage.RequireCapacity(view, lane);
        var state = Authorization.Project(principal, resource.FieldPolicies, record.StateJson, out var omitted);
        var redactedFields = omitted.Select(path => string.Concat(StateRedactionPrefix, path)).ToImmutableArray();
        return new(lane, sagaId, record.Revision, record.Phase, state, record.Deadline,
            redactedFields.Length != 0, redactedFields);
    }

    private static ImmutableArray<string> ProjectRedactedPaths(string[] payload, string[] headers)
    {
        var paths = ImmutableArray.CreateBuilder<string>(payload.Length + headers.Length);
        paths.AddRange(payload.Select(path => string.Concat(PayloadRedactionPrefix, path)));
        paths.AddRange(headers.Select(path => string.Concat(HeaderRedactionPrefix, path)));
        return paths.MoveToImmutable();
    }
}
