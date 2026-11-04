using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Uses one verified connection read budget and its original resource binding through execution publication.</summary>
internal static class UserAnalyticsReadExecution
{

    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.AnalyticsEvents or GrainReadKind.AnalyticsAggregate;

    internal static async Task<GrainValue> ExecuteAsync(DatabaseEngine database, IServiceProvider services,
        PrincipalRecord principal, GrainReadKind kind, DecodedGrainRequest request, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        var work = new ReadExecutionBudget(database.EvaluationClock, token);
        work.AttachResourceLifetimeBinding(binding);
        return kind switch
        {
            GrainReadKind.AnalyticsEvents => new GrainValue(database.ReadAnalyticsEventsBound(principal.Id,
                (GrainPayload.Read<ReadAnalyticsEventsRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.ReadAnalyticsEventsRequest at this closed operation boundary before executing typed code. */), work, binding)) /* KLD0041: The current GrainValue wire slot carries this AnalyticsEvents response with its generated runtime type header. */,
            GrainReadKind.AnalyticsAggregate => new GrainValue(await AggregateAsync(database, services, principal,
                (GrainPayload.Read<AggregateAnalyticsRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.AggregateAnalyticsRequest at this closed operation boundary before executing typed code. */), work, binding, token)) /* KLD0041: The current GrainValue wire slot carries this AnalyticsAggregate response with its generated runtime type header. */,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private static async Task<AnalyticsAggregateResult> AggregateAsync(DatabaseEngine database, IServiceProvider services,
        PrincipalRecord principal, AggregateAnalyticsRequest request, ReadExecutionBudget work,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        using var admission = database.AdmitQuery(token);
        var snapshot = database.CaptureAnalyticsAggregateBound(principal.Id, request, work, binding);
        using var original = await services.GetRequiredService<IUserAnalytics>()
            .AggregateAsync(snapshot, binding, token);
        work.RequireResourceLifetimeBinding(binding);
        work.Check();
        database.ValidateAnalyticsAggregatePublication(snapshot);
        return original.Result;
    }
}
