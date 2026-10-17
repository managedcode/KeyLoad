using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3Trial
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
                var seed = await QueueWorkerRf3Setup.CreateAsync(fixture, deadline.Token);
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
                var seed = await QueueWorkerRf3Setup.CreateAsync(fixture, deadline.Token);
                await QueueWorkerRf3Renewal.RunAsync(fixture, seed, deadline.Token);
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
                var seed = await QueueWorkerRf3ByteSetup.CreateAsync(fixture, deadline.Token);
                await QueueWorkerRf3ByteRefusal.RunAsync(fixture, seed, deadline.Token);
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
                var seed = await QueueWorkerRf3Setup.CreateAsync(fixture, deadline.Token);
                await QueueWorkerRf3RenewalBudget.RunAsync(fixture, seed, deadline.Token);
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
                var seed = await QueueWorkerRf3Setup.CreateAsync(fixture, deadline.Token);
                await QueueWorkerRf3Process.RunAsync(fixture, seed, deadline.Token);
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
                var seed = await QueueWorkerRf3Setup.CreateAsync(fixture, deadline.Token);
                await QueueWorkerRf3HandlerFailure.RunAsync(fixture, seed, deadline.Token);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(ClusterFixture fixture, QueueWorkerRf3Seed seed, bool cancel,
        CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var handlers = new QueueWorkerRf3HandlerLifecycle(cancel);
        var firstReceive = Guid.NewGuid();
        var run = sdk.RunQueueAckWorkerAsync(seed.Lane, firstReceive, handlers.HandleAsync,
            QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.InvocationDeliveries), TimeProvider.System, lifetime.Token);
        try
        {
            await handlers.FirstEntered.Task.WaitAsync(token);
            await handlers.ThirdEntered.Task.WaitAsync(token);
            await Assert.That(run.IsCompleted).IsFalse();
            await QueueWorkerRf3Assertions.LeasedAsync(sdk, mcp, seed,
                handlers.Contexts[QueueWorkerRf3Protocol.First], QueueWorkerRf3Protocol.FirstReadySequence, token);
            await Assert.That(handlers.Entered).IsEqualTo(QueueWorkerRf3Protocol.InvocationDeliveries);
            if (cancel)
            {
                await lifetime.CancelAsync();
            }
            else
            {
                handlers.ReleaseFirst.TrySetResult();
            }
            var result = await run;
            await QueueWorkerRf3Continuation.VerifyAsync(fixture, sdk, mcp, seed, handlers,
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
