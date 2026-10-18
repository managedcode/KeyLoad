using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using D = KeyLoad.UnitTests.Features.ClientApi.DeclaredResponseEofProtocol;
using P = KeyLoad.UnitTests.Features.ClientApi.NativeSuccessBodyProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class NativeSuccessBodyFlow : IAsyncDisposable
{
    private readonly DeclaredResponseEofTasks tasks = new();
    private ConnectionNativeScenario? scenario;
    private KeyLoadClientKestrelServer? server;
    private bool transportUncertain;
    private readonly Lock disposalGate = new();
    private Task? disposal;
    internal static async Task RunAsync(string body, bool appendNativePrefix = false, bool declareLength = true)
    {
        if (!appendNativePrefix && body is not P.NullBody and not P.MalformedBody)
        {
            throw new InvalidOperationException(P.Failure);
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new NativeSuccessBodyFlow();
            await ServerFailureObserver.ObserveAsync(() => owner.RunOwnedAsync(body, appendNativePrefix, declareLength), failures);
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
        await tasks.JoinRemainingAsync(failures);
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

    private async Task RunOwnedAsync(string body, bool appendNativePrefix, bool declareLength)
    {
        scenario = new ConnectionNativeScenario();
        await scenario.InitializeAsync(TestContext.Current!.Execution.CancellationToken);
        using var deadline = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        var token = linked.Token;
        var principal = scenario.Principal();
        await Assert.That(scenario.Fixture.Database.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(
            global::KeyLoad.Core.DatabaseEngine.Credential(P.KeyId, principal.Id, P.ApiKey))).Get<bool>()).IsTrue();
        await DeclaredResponseEofAssertions.EmptyAsync(scenario);
        var options = UnitClientOptions.Execution();
        var endpoint = new NativeSuccessBodyEndpoint(scenario, principal, tasks, body,
            appendNativePrefix, declareLength, options.Value.MaximumResponseBodyBytes);
        await Assert.That(endpoint.Healthy.CommandId).IsNotEqualTo(endpoint.Original.CommandId);
        var placement = scenario.Fixture.Database.Database.ReadAtomicPartitionPlacement(D.Root,
            endpoint.Original.Partition);
        transportUncertain = true;
        server = await KeyLoadClientKestrelServer.StartAsync(endpoint.HandleAsync);
        transportUncertain = false;
        var client = new KeyLoadClient(server.Client, P.ApiKey, options);
        await NativeSuccessBodyStages.RunAsync(new(scenario, principal, endpoint, client, tasks, placement), token);
        await scenario.CloseAsync(token);
    }
}
