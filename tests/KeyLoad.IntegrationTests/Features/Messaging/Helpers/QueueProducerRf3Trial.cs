using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3Trial
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
                var seed = await QueueProducerRf3Setup.CreateAsync(fixture, deadline.Token);
                var original = await QueueProducerRf3OriginalFlow.ExecuteAsync(fixture, seed, cancelAfterResponse, failures, deadline.Token);
                ServerFailureObserver.ThrowIfAny(failures);
                ArgumentNullException.ThrowIfNull(original);
                var outbox = await AtomicProducerRf3Outbox.ReadAsync(fixture, seed, deadline.Token);
                await AtomicProducerRf3Outbox.LiteralAsync(outbox, seed.Original.Mutations.Length);
                await AtomicProducerRf3Index.RequireAsync(fixture, seed, false, deadline.Token);
                await QueueProducerRf3Refusals.ExecuteAsync(fixture, original, failures, deadline.Token);
                ServerFailureObserver.ThrowIfAny(failures);
                var documentRefusal = AtomicProducerRf3DocumentRefusal.Create(seed);
                var refusalProblem = await AtomicProducerRf3DocumentRefusal.ExecuteAsync(fixture, original,
                    documentRefusal, null, failures, deadline.Token);
                await AtomicProducerRf3Outbox.SameAsync(fixture, seed, outbox, deadline.Token);
                await AtomicProducerRf3Index.RequireAsync(fixture, seed, false, deadline.Token);
                await QueueProducerRf3Cold.RestartAsync(fixture, deadline.Token);
                _ = await AtomicProducerRf3DocumentRefusal.ExecuteAsync(fixture, original,
                    documentRefusal, refusalProblem, failures, deadline.Token);
                await QueueProducerRf3Replay.ExecuteAsync(fixture, original, failures, deadline.Token);
                ServerFailureObserver.ThrowIfAny(failures);
                await AtomicProducerRf3Outbox.SameAsync(fixture, seed, outbox, deadline.Token);
                await AtomicProducerRf3Index.RequireAsync(fixture, seed, false, deadline.Token);
                await QueueProducerRf3Policy.RestoreAsync(fixture, original, failures, deadline.Token);
                ServerFailureObserver.ThrowIfAny(failures);
                var healthy = await QueueProducerRf3HealthyFlow.ExecuteAsync(fixture, original, failures, deadline.Token);
                ServerFailureObserver.ThrowIfAny(failures);
                ArgumentNullException.ThrowIfNull(healthy);
                var finalOutbox = await AtomicProducerRf3Outbox.ReadAsync(fixture, seed, deadline.Token);
                await AtomicProducerRf3Outbox.LiteralAsync(finalOutbox,
                    seed.Original.Mutations.Length + healthy.Command.Mutations.Length);
                await Assert.That(finalOutbox.Head.StoredBytes > outbox.Head.StoredBytes).IsTrue();
                await AtomicProducerRf3Index.RequireAsync(fixture, seed, true, deadline.Token);
                await QueueProducerRf3Cold.RestartAsync(fixture, deadline.Token);
                await QueueProducerRf3Continuation.ExecuteAsync(fixture, healthy, failures, deadline.Token);
                await AtomicProducerRf3Outbox.SameAsync(fixture, seed, finalOutbox, deadline.Token);
                await AtomicProducerRf3Index.RequireAsync(fixture, seed, true, deadline.Token);
                ServerFailureObserver.ThrowIfAny(failures);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
