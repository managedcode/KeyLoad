using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Exposes current-policy inspection of retained schedule and saga state.</summary>
public static class RecurringSagaClientExtensions
{
    private const string SchedulePath = "/v1/queues/schedules/inspect";
    private const string SagaPath = "/v1/queues/sagas/inspect";

    /// <summary>Reads one projected recurring schedule, or null when absent.</summary>
    public static Task<Result<RecurringScheduleInspection?>> InspectRecurringScheduleAsync(this KeyLoadClient client,
        InspectRecurringScheduleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<RecurringScheduleInspection?>(SchedulePath, request, cancellationToken);
    }

    /// <summary>Reads projected saga state without exposing its timeout template.</summary>
    public static Task<Result<SagaInspection?>> InspectSagaAsync(this KeyLoadClient client,
        InspectSagaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<SagaInspection?>(SagaPath, request, cancellationToken);
    }
}
