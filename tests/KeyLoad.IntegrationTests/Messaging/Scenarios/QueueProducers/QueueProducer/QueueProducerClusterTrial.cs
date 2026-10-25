using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueProducerClusterTrial
{
    internal static async Task RunAsync(bool cancelAfterResponse)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                var seed = await QueueProducerClusterSetup.CreateAsync(fixture, deadline.Token);
                var original = (await QueueProducerClusterOriginalFlow.ExecuteAsync(fixture, seed, cancelAfterResponse, failures, deadline.Token)).Value;
                ServerFailureObserver.ThrowIfAny(failures);
                ArgumentNullException.ThrowIfNull(original);
                var outbox = await AtomicProducerClusterOutbox.ReadAsync(fixture, seed, deadline.Token);
                await AtomicProducerClusterOutbox.LiteralAsync(outbox, seed.Original.Mutations.Length);
                await AtomicProducerClusterIndex.RequireAsync(fixture, seed, false, deadline.Token);
                await QueueProducerClusterRefusals.ExecuteAsync(fixture, original, failures, deadline.Token);
                ServerFailureObserver.ThrowIfAny(failures);
                var documentRefusal = AtomicProducerClusterDocumentRefusal.Create(seed);
                var refusalProblem = await AtomicProducerClusterDocumentRefusal.ExecuteAsync(fixture, original,
                    documentRefusal, null, failures, deadline.Token);
                await AtomicProducerClusterOutbox.SameAsync(fixture, seed, outbox, deadline.Token);
                await AtomicProducerClusterIndex.RequireAsync(fixture, seed, false, deadline.Token);
                await QueueProducerClusterCold.RestartAsync(fixture, deadline.Token);
                _ = await AtomicProducerClusterDocumentRefusal.ExecuteAsync(fixture, original,
                    documentRefusal, refusalProblem, failures, deadline.Token);
                await QueueProducerClusterReplay.ExecuteAsync(fixture, original, failures, deadline.Token);
                ServerFailureObserver.ThrowIfAny(failures);
                await AtomicProducerClusterOutbox.SameAsync(fixture, seed, outbox, deadline.Token);
                await AtomicProducerClusterIndex.RequireAsync(fixture, seed, false, deadline.Token);
                await QueueProducerClusterPolicy.RestoreAsync(fixture, original, failures, deadline.Token);
                ServerFailureObserver.ThrowIfAny(failures);
                var healthy = await QueueProducerClusterHealthyFlow.ExecuteAsync(fixture, original, failures, deadline.Token);
                ServerFailureObserver.ThrowIfAny(failures);
                ArgumentNullException.ThrowIfNull(healthy);
                var finalOutbox = await AtomicProducerClusterOutbox.ReadAsync(fixture, seed, deadline.Token);
                await AtomicProducerClusterOutbox.LiteralAsync(finalOutbox,
                    seed.Original.Mutations.Length + healthy.Command.Mutations.Length);
                await Assert.That(finalOutbox.Head.StoredBytes > outbox.Head.StoredBytes).IsTrue();
                await AtomicProducerClusterIndex.RequireAsync(fixture, seed, true, deadline.Token);
                await QueueProducerClusterCold.RestartAsync(fixture, deadline.Token);
                await QueueProducerClusterContinuation.ExecuteAsync(fixture, healthy, failures, deadline.Token);
                await AtomicProducerClusterOutbox.SameAsync(fixture, seed, finalOutbox, deadline.Token);
                await AtomicProducerClusterIndex.RequireAsync(fixture, seed, true, deadline.Token);
                ServerFailureObserver.ThrowIfAny(failures);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
