using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class NativeCredentialStatusFlow : IAsyncDisposable
{
    private ConnectionNativeScenario? scenario;
    private KeyLoadClientKestrelServer? server;
    private NativeCredentialStatusState? state;
    private bool transportUncertain;
    private readonly Lock disposalGate = new();
    private Task? disposal;

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new NativeCredentialStatusFlow();
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
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
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
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task RunOwnedAsync()
    {
        scenario = new ConnectionNativeScenario(nativeReplicaAdmission: true);
        using var deadline = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        var token = linked.Token;
        await scenario.InitializeAsync(token);
        state = await NativeCredentialStatusSeed.CreateAsync(scenario);
        var endpoint = new NativeCredentialStatusEndpoint(state);
        transportUncertain = true;
        server = await KeyLoadClientKestrelServer.StartAsync(endpoint.HandleAsync);
        transportUncertain = false;
        await NativeCredentialStatusStages.RunAsync(state, endpoint, server.Client, token);
        await scenario.CloseAsync(token);
    }
}
