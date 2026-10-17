using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ModelRootPolicyFlow
{
    internal static async Task RunAsync(bool queue, bool headerRoot, CancellationToken token)
    {
        var failures = new List<Exception>();
        ModelRootPolicyFixture? fixture = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new(queue, headerRoot);
            await fixture.InitializeAsync();
            await ModelRootPolicyAssertions.WaveAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, false, false, token);
            await fixture.ConfigureAsync(2L, true, false);
            await ModelRootPolicyAssertions.WaveAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, true, false, token);
            await fixture.ConfigureAsync(3L, false, false);
            await ModelRootPolicyAssertions.WaveAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, false, false, token);
            await fixture.ConfigureAsync(4L, true, true);
            await ModelRootPolicyAssertions.WaveAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, true, true, token);
            await ReplayAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, token);
            await ModelRootPolicyLifetime.ColdAsync(fixture, token);
        }, failures);
        ServerFailureObserver.Observe(() => fixture?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static Task ReplayAsync(ModelRootPolicyFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => ModelRootPolicyLifetime.ObserveAsync(store, async () =>
        {
            var operation = new ReplicatedOperation(fixture.Seed.CommandId, OperationKind.Batch, "root", default,
                System.Text.Json.JsonSerializer.Serialize(fixture.Seed, JsonDefaults.Options));
            var replay = database.ApplyEmbedded(operation, cancellationToken: token).Get<CommitReceipt>();
            await ModelRootPolicyAssertions.EqualAsync(replay, fixture.Receipt);
            foreach (var mutation in replay.Mutations)
            {
                await Assert.That(mutation.CompositionReferences).IsEmpty();
            }
            var bytes = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(fixture.Harness.Partition, "root", fixture.Seed.CommandId)))!;
            await Assert.That(bytes.AsSpan().SequenceEqual(fixture.Outcome)).IsTrue();
        });
}
