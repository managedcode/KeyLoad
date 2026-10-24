using KeyLoad.Clustering.Search;
using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Search;

internal sealed class TextOnlineTestRuntime : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private readonly TextIncrementalMaintenanceService explicitOwner;
    private readonly ITextProjection projection;
    internal TextOnlineMaintenanceService Owner
    {
        get;
    }
    internal ServerRuntimeOptions Options
    {
        get;
    }
    internal SearchEngine Search
    {
        get;
    }
    internal TextOnlineTestRuntime(TestDatabase fixture)
    {
        var registrations = new ServiceCollection().AddRuntimeOptions(new ConfigurationBuilder().Build());
        registrations.AddSingleton(UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits));
        services = registrations.BuildServiceProvider();
        try
        {
            Options = services.GetRequiredService<ServerRuntimeOptions>();
            var actual = TextHostSearch.Open(fixture.Database, fixture.Directory, Options, fixture.Database.EvaluationClock);
            projection = actual.Projection;
            explicitOwner = actual.Explicit;
            Owner = actual.Online;
            Search = new(fixture.Database, Options.Core.QueryExecution, projection);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(services.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal DateTimeOffset FreshExpiry(TestDatabase fixture)
        => fixture.Database.EvaluationClock.GetUtcNow().AddSeconds(Options.Core.DatabaseLimits.Value.QueryDeadlineSeconds);

    internal Task<OnlineTextCapabilityResult> PhaseAsync(TestDatabase fixture, Guid session,
        OnlineTextIndexMaintenanceRequest request, OnlineTextCapabilityKind kind, DateTimeOffset expiry,
        CancellationToken token, ProjectionBatch? page = null, CommitProjectionBatchRequest? intent = null,
        ProjectionBatchResult? receipt = null)
    {
        var principal = fixture.Store.Read(view => fixture.Database.Principal(view,
            TextMaintenanceTestValues.Principal, fixture.Database.EvaluationClock.GetUtcNow()));
        return Owner.ExecuteAsync(principal, new(session, request, kind, page, intent, receipt), expiry, token);
    }

    internal ITextProjectionLease BorrowCurrent(TestDatabase fixture, SearchRequest request, ReadExecutionBudget budget)
        => fixture.Database.WithQueryView(TextMaintenanceTestValues.Principal, request.Partition, request.Collection,
            (view, principal, resource) => ((ICurrentTextProjection)projection).AcquireCurrent(view, principal, resource, request, budget));

    internal ICapturedTextRead CaptureForBudgetTrial(TestDatabase fixture, SearchRequest request, KeyLoad.Core.ReadExecutionBudget budget)
        => fixture.Database.WithQueryView(TextMaintenanceTestValues.Principal, request.Partition, request.Collection,
            (view, principal, resource) => ((ICapturedTextProjection)projection).CaptureRead(view, principal, resource, request, budget));

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => Owner.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => explicitOwner.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(projection.Dispose, failures);
        Task providerDisposal;
        try
        {
            providerDisposal = services.DisposeAsync().AsTask();
        }
        catch (Exception error)
        {
            // Every prior owner has settled; rethrow this actual final invocation failure with the ordered ledger.
            failures.Add(error);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        await providerDisposal.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (providerDisposal.Exception is { } providerFailure)
        {
            failures.AddRange(providerFailure.InnerExceptions);
        }
        else if (providerDisposal.IsCanceled)
        {
            failures.Add(new TaskCanceledException(providerDisposal));
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
