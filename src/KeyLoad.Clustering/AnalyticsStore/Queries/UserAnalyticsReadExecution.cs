using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Uses one signed connection read budget and its original resource binding through native publication.</summary>
internal static class UserAnalyticsReadExecution
{
    private const string DeadlineExceeded = "The read execution deadline is exceeded.";

    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.AnalyticsEvents or GrainReadKind.AnalyticsAggregate;

    internal static async Task<object?> ExecuteAsync(DatabaseEngine database, IServiceProvider services,
        PrincipalRecord principal, GrainReadKind kind, DecodedGrainRequest request, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
        work.ConstrainLifetime(request.Envelope.ExpiresAt);
        work.AttachResourceLifetimeBinding(binding);
        return kind switch
        {
            GrainReadKind.AnalyticsEvents => database.ReadAnalyticsEventsBound(principal.Id,
                GrainNativePayload.Read<ReadAnalyticsEventsRequest>(request.Payload), work, binding),
            GrainReadKind.AnalyticsAggregate => await AggregateAsync(database, services, principal,
                GrainNativePayload.Read<AggregateAnalyticsRequest>(request.Payload), work, binding, token).ConfigureAwait(true),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private static async Task<AnalyticsAggregateResult> AggregateAsync(DatabaseEngine database, IServiceProvider services,
        PrincipalRecord principal, AggregateAnalyticsRequest request, ReadExecutionBudget work,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        using var admission = database.AdmitQuery(token);
        var snapshot = database.CaptureAnalyticsAggregateBound(principal.Id, request, work, binding);
        using var original = await services.GetRequiredService<INativeUserAnalytics>()
            .AggregateAsync(snapshot, binding, token).ConfigureAwait(true);
        work.RequireResourceLifetimeBinding(binding);
        using var deadline = new CancellationTokenSource(work.RemainingLifetime, snapshot.Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, work.Cancellation, deadline.Token);
        try
        {
            await services.GetRequiredService<Func<ICommitCoordinator>>()().ReadBarrierAsync(linked.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested
            && !token.IsCancellationRequested && !work.Cancellation.IsCancellationRequested)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceeded, error);
        }
        database.ValidateAnalyticsAggregatePublication(snapshot);
        work.CheckResult(original.Result);
        return original.Result;
    }
}
