using KeyLoad.Server;
using KeyLoad.UnitTests.Features.OrleansExecution;
using static KeyLoad.UnitTests.Features.ClientApi.DeclaredResponseEofProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class DeclaredResponseEofFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        var tasks = new DeclaredResponseEofTasks();
        ConnectionScenario? scenario = null;
        KeyLoadClientKestrelServer? server = null;
        DeclaredResponseEofEndpoint? endpoint = null;
        var transportUncertain = false;
        await ServerFailureObserver.ObserveAsync(RunOwnedAsync, failures);
        endpoint?.Write.Release.TrySetResult();
        endpoint?.Read.Release.TrySetResult();
        if (server is not null)
        {
            transportUncertain = true;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await server.DisposeAsync();
                transportUncertain = false;
            }, failures);
        }
        await tasks.JoinRemainingAsync(failures);
        if (scenario is not null && !transportUncertain)
        {
            await ServerFailureObserver.ObserveAsync(() => scenario.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);

        async Task RunOwnedAsync()
        {
            scenario = new ConnectionScenario();
            await scenario.InitializeAsync(TestContext.Current!.Execution.CancellationToken);
            using var deadline = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            var token = linked.Token;
            var principal = scenario.Principal();
            await Assert.That(scenario.Fixture.Database.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(
                global::KeyLoad.Core.AuthorizationExecution.Credential(KeyId, principal.Id, ApiKey))).Get<bool>()).IsTrue();
            await DeclaredResponseEofAssertions.EmptyAsync(scenario);
            endpoint = new(scenario, principal, tasks);
            var placement = scenario.Fixture.Database.Database.ReadAtomicPartitionPlacement(Root,
                endpoint.Command.Partition);
            transportUncertain = true;
            server = await KeyLoadClientKestrelServer.StartAsync(endpoint.HandleAsync);
            transportUncertain = false;
            await DeclaredResponseEofStages.RunAsync(scenario, principal, server, endpoint, tasks, placement, token);
            await scenario.CloseAsync(token);
        }
    }
}
