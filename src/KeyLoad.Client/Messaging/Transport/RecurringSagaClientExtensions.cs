using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Exposes current-policy inspection of retained schedule and saga state.</summary>
public static class RecurringSagaClientExtensions
{
    private const string SchedulePath = "/queues/schedules/inspect";
    private const string SagaPath = "/queues/sagas/inspect";

    /// <summary>Reads a response whose Schedule field contains projected recurring state, or null when absent.</summary>
    public static Task<Result<RecurringScheduleReadResult>> InspectRecurringScheduleAsync(this KeyLoadClient client,
        InspectRecurringScheduleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request InspectRecurringScheduleRequest reference through its object parameter. */;
        return client.Send<RecurringScheduleReadResult, InspectRecurringScheduleRequest>(SchedulePath, request, false, null, cancellationToken);
    }

    /// <summary>Reads a response whose Saga field is null when absent, without exposing its timeout template.</summary>
    public static Task<Result<SagaReadResult>> InspectSagaAsync(this KeyLoadClient client,
        InspectSagaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request InspectSagaRequest reference through its object parameter. */;
        return client.Send<SagaReadResult, InspectSagaRequest>(SagaPath, request, false, null, cancellationToken);
    }
}
