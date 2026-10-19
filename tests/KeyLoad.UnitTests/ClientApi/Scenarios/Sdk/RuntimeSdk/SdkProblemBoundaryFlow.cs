using KeyLoad.Server;
using KeyLoad.UnitTests.Features.OrleansExecution;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class SdkProblemBoundaryFlow
{
    internal static async Task RunAsync(bool declared)
    {
        var failures = new List<Exception>();
        var tasks = new SdkProblemBoundaryTasks();
        ConnectionScenario? execution = null;
        KeyLoadClientKestrelServer? server = null;
        var transportUncertain = false;
        await ServerFailureObserver.ObserveAsync(RunOwnedAsync, failures);
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
        if (execution is not null && !transportUncertain)
        {
            await ServerFailureObserver.ObserveAsync(() => execution.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);

        async Task RunOwnedAsync()
        {
            execution = new ConnectionScenario();
            var state = new SdkProblemBoundaryScenario(execution, tasks);
            await state.InitializeAsync(TestContext.Current!.Execution.CancellationToken);
            using var deadline = new CancellationTokenSource(execution.Timing.CompletionTimeout, TimeProvider.System);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            var endpoint = new SdkProblemBoundaryEndpoint(state, tasks, declared);
            transportUncertain = true;
            server = await KeyLoadClientKestrelServer.StartAsync(endpoint.HandleAsync);
            transportUncertain = false;
            await SdkProblemBoundaryStages.RunAsync(state, server, tasks, declared, linked.Token);
            await execution.CloseAsync(linked.Token);
        }
    }
}
