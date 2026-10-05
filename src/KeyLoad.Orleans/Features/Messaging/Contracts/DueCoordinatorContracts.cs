using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Orleans;

internal static class DueCoordinatorAliases
{
    internal const string Service = "keyload.orleans.messaging.due-service.v1";
    internal const string Coordinator = "keyload.orleans.messaging.due-coordinator.v1";
    internal const string Result = "keyload.orleans.messaging.due-dispatch-result.v1";
    internal const string Process = nameof(IRecurringDueCoordinatorGrain.ProcessDueAsync);
    internal const int CoordinatorInterfaceVersion = 1;
}

internal static class DueCoordinatorFields
{
    internal const int FirstRevision = 1;
    internal const int FirstScheduleGeneration = 1;
    internal const int NoGeneration = 0;
    internal const int FirstOrdinal = 0;

    internal const int Error = 0;
    internal const string InvalidHint = "The due-work hint does not match this coordinator partition.";
}

[global::Orleans.Alias(DueCoordinatorAliases.Service)]
internal interface IRecurringDueGrainService : global::Orleans.Services.IGrainService
{
}

[global::Orleans.Alias(DueCoordinatorAliases.Coordinator),
 global::Orleans.CodeGeneration.Version(DueCoordinatorAliases.CoordinatorInterfaceVersion)]
internal interface IRecurringDueCoordinatorGrain : global::Orleans.IGrainWithStringKey
{
    [global::Orleans.Alias(DueCoordinatorAliases.Process)]
    Task<DueDispatchResult> ProcessDueAsync(DueWorkHint hint, CancellationToken cancellationToken);
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(DueCoordinatorAliases.Result)]
internal sealed record DueDispatchResult([property: global::Orleans.Id(DueCoordinatorFields.Error)] ErrorCode? Error);
