using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class SdkNativeProblemBoundaryFlow
{
    internal static async Task RunAsync(bool declared)
    {
        var failures = new List<Exception>();
        var tasks = new SdkNativeProblemBoundaryTasks();
        ConnectionNativeScenario? native = null;
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
        if (native is not null && !transportUncertain)
        {
            await ServerFailureObserver.ObserveAsync(() => native.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);

        async Task RunOwnedAsync()
        {
            native = new ConnectionNativeScenario();
            var state = new SdkNativeProblemBoundaryScenario(native, tasks);
            await state.InitializeAsync(TestContext.Current!.Execution.CancellationToken);
            using var deadline = new CancellationTokenSource(native.Timing.CompletionTimeout, TimeProvider.System);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            var endpoint = new SdkNativeProblemBoundaryEndpoint(state, tasks, declared);
            transportUncertain = true;
            server = await KeyLoadClientKestrelServer.StartAsync(endpoint.HandleAsync);
            transportUncertain = false;
            await SdkNativeProblemBoundaryStages.RunAsync(state, server, tasks, declared, linked.Token);
            await native.CloseAsync(linked.Token);
        }
    }
}
