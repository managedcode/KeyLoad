using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class CredentialStatusFlow : IAsyncDisposable
{
    private ConnectionScenario? scenario;
    private KeyLoadClientKestrelServer? server;
    private CredentialStatusState? state;
    private bool transportUncertain;
    private readonly Lock disposalGate = new();
    private Task? disposal;

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new CredentialStatusFlow();
            await ServerFailureObserver.ObserveAsync(() => owner.RunOwnedAsync(), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        if (server is not null)
        {
            transportUncertain = true;
            try
            {
                await server.DisposeAsync();
                transportUncertain = false;
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        if (state is not null)
        {
            await state.Tasks.JoinRemainingAsync(failures);
        }
        if (scenario is not null && !transportUncertain)
        {
            try
            {
                await scenario.DisposeAsync();
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task RunOwnedAsync()
    {
        scenario = new ConnectionScenario();
        using var deadline = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        var token = linked.Token;
        await scenario.InitializeAsync(token);
        state = await CredentialStatusSeed.CreateAsync(scenario);
        var endpoint = new CredentialStatusEndpoint(state);
        transportUncertain = true;
        server = await KeyLoadClientKestrelServer.StartAsync(endpoint.HandleAsync);
        transportUncertain = false;
        await CredentialStatusStages.RunAsync(state, endpoint, server.Client, token);
        await scenario.CloseAsync(token);
    }
}
