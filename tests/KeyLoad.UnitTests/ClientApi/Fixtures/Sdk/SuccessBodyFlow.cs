using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.OrleansExecution;
using D = KeyLoad.UnitTests.Features.ClientApi.DeclaredResponseEofProtocol;
using P = KeyLoad.UnitTests.Features.ClientApi.SuccessBodyProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class SuccessBodyFlow : IAsyncDisposable
{
    private readonly DeclaredResponseEofTasks tasks = new();
    private ConnectionScenario? scenario;
    private KeyLoadClientKestrelServer? server;
    private bool transportUncertain;
    private readonly Lock disposalGate = new();
    private Task? disposal;
    internal static async Task RunAsync(string body, bool appendPrefix = false, bool declareLength = true)
    {
        if (!appendPrefix && body is not P.NullBody and not P.MalformedBody)
        {
            throw new InvalidOperationException(P.Failure);
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new SuccessBodyFlow();
            await ServerFailureObserver.ObserveAsync(() => owner.RunOwnedAsync(body, appendPrefix, declareLength), failures);
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
        await tasks.JoinRemainingAsync(failures);
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

    private async Task RunOwnedAsync(string body, bool appendPrefix, bool declareLength)
    {
        scenario = new ConnectionScenario();
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
        var endpoint = new SuccessBodyEndpoint(scenario, principal, tasks, body,
            appendPrefix, declareLength, options.Value.MaximumResponseBodyBytes);
        await Assert.That(endpoint.Healthy.CommandId).IsNotEqualTo(endpoint.Original.CommandId);
        var placement = scenario.Fixture.Database.Database.ReadAtomicPartitionPlacement(D.Root,
            endpoint.Original.Partition);
        transportUncertain = true;
        server = await KeyLoadClientKestrelServer.StartAsync(endpoint.HandleAsync);
        transportUncertain = false;
        var client = new KeyLoadClient(server.Client, P.ApiKey, options);
        await SuccessBodyStages.RunAsync(new(scenario, principal, endpoint, client, tasks, placement), token);
        await scenario.CloseAsync(token);
    }
}
