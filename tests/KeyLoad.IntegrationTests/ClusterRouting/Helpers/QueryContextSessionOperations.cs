using KeyLoad.Clustering;
using KeyLoad.Server;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class QueryContextSessionOperations : IAsyncDisposable
{
    private const string MissingSelected = "The original selected query owner is absent.";
    private const string MissingSibling = "The independent original sibling query owner is absent.";
    private const string CancelledRead = "The read response is unavailable.";

    private readonly ConnectionRf3Scenario scenario;
    private readonly CancellationTokenSource selected;
    private readonly CancellationTokenSource sibling;
    private readonly CancellationToken token;
    private bool selectedSettled;
    private bool siblingSettled;
    internal ConnectionRf3Caller Caller
    {
        get;
    }
    internal ConnectionRf3HeldOperation<Result<QueryPage>>? First
    {
        get;
        private set;
    }
    internal ConnectionRf3HeldOperation<Result<QueryPage>>? Second
    {
        get;
        private set;
    }
    internal Task<Result<QueryPage>>? FirstOriginal
    {
        get;
        private set;
    }
    internal Task<Result<QueryPage>>? SecondOriginal
    {
        get;
        private set;
    }
    internal QueryContextSessionOperations(ConnectionRf3Scenario scenario, ConnectionRf3Caller caller, CancellationToken token)
    {
        this.scenario = scenario;
        Caller = caller;
        this.token = token;
        selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            sibling = CancellationTokenSource.CreateLinkedTokenSource(token);
        }
        catch (Exception)
        {
            selected.Dispose();
            throw;
        }
    }
    internal async Task StartAsync(ConnectionQueryContextState state)
    {
        var first = await scenario.StartHeldAsync(state.First.Identity, Guid.Empty, GrainReadKind.Query,
            () => FirstOriginal = Caller.Sdk.QueryAsync(ConnectionQueryContextRequests.Sql(state.First.Identity.Partition), selected.Token), token);
        First = first;
        var second = await scenario.StartHeldAsync(state.First.Identity, Guid.Empty, GrainReadKind.AstQuery,
            () => SecondOriginal = Caller.Sdk.QueryAstAsync(ConnectionQueryContextRequests.Ast(state.First.Identity.Partition), sibling.Token), token);
        Second = second;
        await ConnectionRf3WitnessReader.SameOwnerAsync(first.Witness, second.Witness);
        await Assert.That(ReferenceEquals(first.Original, second.Original)).IsFalse();
        await Assert.That(first.Original.IsCompleted).IsFalse();
        await Assert.That(second.Original.IsCompleted).IsFalse();
    }
    internal async Task CancelSelectedAsync()
    {
        var first = First ?? throw new InvalidOperationException(MissingSelected);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(selected.CancelAsync, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = await first.Original;
            await Assert.That(actual.IsSuccess).IsFalse();
            await Assert.That(actual.Value).IsNull();
            await KeyLoad.IntegrationTests.Features.QueryExecution.SqlRf3Protocol.EqualAsync(
                Errors.Problem(ErrorCode.Cancelled, CancelledRead), actual.Problem);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await scenario.SettleAsync(first.Arm, first.Marker, token);
            selectedSettled = true;
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal async Task<ConnectionQueryContextRead> ReleaseSiblingAsync()
    {
        var second = Second ?? throw new InvalidOperationException(MissingSibling);
        scenario.Controls.WriteRelease(second.Arm, second.Marker.RequestId);
        var actual = await second.Original;
        await scenario.SettleAsync(second.Arm, second.Marker, token);
        siblingSettled = true;
        return new(await KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(actual), second.Witness, second.Marker);
    }
    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(selected.CancelAsync, failures);
        await ServerFailureObserver.ObserveAsync(sibling.CancelAsync, failures);
        if (FirstOriginal is { IsCompleted: false } || SecondOriginal is { IsCompleted: false })
        {
            Caller.AbortTransport(failures);
        }
        if (FirstOriginal is { } firstOriginal)
        {
            await ServerFailureObserver.ObserveAsync(() => firstOriginal, failures);
        }
        if (SecondOriginal is { } secondOriginal)
        {
            await ServerFailureObserver.ObserveAsync(() => secondOriginal, failures);
        }
        await ServerFailureObserver.ObserveAsync(SettleRemainingAsync, failures);
        try
        {
            sibling.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        try
        {
            selected.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private async Task SettleRemainingAsync()
    {
        var failures = new List<Exception>();
        if (First is { } first && !selectedSettled)
        {
            await ServerFailureObserver.ObserveAsync(() => scenario.SettleAsync(first.Arm, first.Marker, token), failures);
        }
        if (Second is { } second && !siblingSettled)
        {
            await ServerFailureObserver.ObserveAsync(() => scenario.SettleAsync(second.Arm, second.Marker, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
