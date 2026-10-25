using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueWorkerClusterTrial
{
    internal static async Task RunAsync(bool cancel)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                var seed = await QueueWorkerClusterSetup.CreateAsync(fixture, deadline.Token);
                await ExecuteAsync(fixture, seed, cancel, deadline.Token);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task RenewalAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                var seed = await QueueWorkerClusterSetup.CreateAsync(fixture, deadline.Token);
                await QueueWorkerClusterRenewal.RunAsync(fixture, seed, deadline.Token);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ByteRefusalAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                var seed = await QueueWorkerClusterByteSetup.CreateAsync(fixture, deadline.Token);
                await QueueWorkerClusterByteRefusal.RunAsync(fixture, seed, deadline.Token);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task RenewalBudgetAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                var seed = await QueueWorkerClusterSetup.CreateAsync(fixture, deadline.Token);
                await QueueWorkerClusterRenewalBudget.RunAsync(fixture, seed, deadline.Token);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ProcessAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                var seed = await QueueWorkerClusterSetup.CreateAsync(fixture, deadline.Token);
                await QueueWorkerClusterProcess.RunAsync(fixture, seed, deadline.Token);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task HandlerFailureAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                var seed = await QueueWorkerClusterSetup.CreateAsync(fixture, deadline.Token);
                await QueueWorkerClusterHandlerFailure.RunAsync(fixture, seed, deadline.Token);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(ClusterFixture fixture, QueueWorkerClusterSeed seed, bool cancel,
        CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var handlers = new QueueWorkerClusterHandlerLifecycle(cancel);
        var firstReceive = Guid.NewGuid();
        var run = sdk.RunQueueAckWorkerAsync(seed.Lane, firstReceive, handlers.HandleAsync,
            QueueWorkerClusterProtocol.Options(QueueWorkerClusterProtocol.InvocationDeliveries), TimeProvider.System, lifetime.Token);
        try
        {
            await handlers.FirstEntered.Task.WaitAsync(token);
            await handlers.ThirdEntered.Task.WaitAsync(token);
            await Assert.That(run.IsCompleted).IsFalse();
            await QueueWorkerClusterAssertions.LeasedAsync(sdk, mcp, seed,
                handlers.Contexts[QueueWorkerClusterProtocol.First], QueueWorkerClusterProtocol.FirstReadySequence, token);
            await Assert.That(handlers.Entered).IsEqualTo(QueueWorkerClusterProtocol.InvocationDeliveries);
            if (cancel)
            {
                await lifetime.CancelAsync();
            }
            else
            {
                handlers.ReleaseFirst.TrySetResult();
            }
            var result = await run;
            await QueueWorkerClusterContinuation.VerifyAsync(fixture, sdk, mcp, seed, handlers,
                result, firstReceive, cancel, token);
        }
        finally
        {
            await lifetime.CancelAsync();
            handlers.ReleaseFirst.TrySetResult();
            handlers.ReleaseThird.TrySetResult();
            await run;
        }
    }
}
