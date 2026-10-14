using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal static class ReplicaPoisonedInstallColdAssertions
{
    private const int MaximumImageRecords = 5;
    private const long TailIndex = 2;
    private const long TailTerm = 2;

    internal static async Task RunAsync(ReplicaConfiguration configuration, StoreIdentity identity,
        long position, ReplicaHardState originalState, byte[] originalImage)
    {
        byte[]? healthyImage = null;
        var healthyState = originalState with
        {
            LastIndex = TailIndex,
            CommittedIndex = TailIndex
        };
        await WithOwnerAsync(configuration, async (store, log) =>
        {
            await VerifyAsync(store, log, identity, position, originalState, originalImage, 4);
            log.Append([new(TailIndex, TailTerm, null)]);
            log.Commit(TailIndex);
            ReplicaTermMetadataFixture.WriteAppliedCut(store, TailIndex);
            await Assert.That(log.TermAt(TailIndex)).IsEqualTo(TailTerm);
            await Assert.That(Convert.ToHexString(ReplicaProtocolCodec.Serialize(log.State)))
                .IsEqualTo(Convert.ToHexString(ReplicaProtocolCodec.Serialize(healthyState)));
            healthyImage = await CaptureAsync(store, MaximumImageRecords);
            position = checked(position + 3);
            await Assert.That(store.Position).IsEqualTo(position);
        });
        await WithOwnerAsync(configuration, async (store, log) =>
        {
            await VerifyAsync(store, log, identity, position, healthyState, healthyImage!, MaximumImageRecords);
            await Assert.That(log.TermAt(TailIndex)).IsEqualTo(TailTerm);
        });
    }

    internal static async Task<byte[]> CaptureAsync(ZoneTreeStore store, int records)
    {
        var page = store.Read(view => view.Scan([], MaximumImageRecords));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(records);
        return JsonDefaults.Serialize(page);
    }

    private static async Task VerifyAsync(ZoneTreeStore store, DurableReplicaLog log, StoreIdentity identity,
        long position, ReplicaHardState state, byte[] image, int records)
    {
        await Assert.That(store.Identity.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
        await Assert.That(store.Identity.ReadGeneration).IsEqualTo(identity.ReadGeneration + 1);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(Convert.ToHexString(await CaptureAsync(store, records))).IsEqualTo(Convert.ToHexString(image));
        await Assert.That(Convert.ToHexString(ReplicaProtocolCodec.Serialize(log.State)))
            .IsEqualTo(Convert.ToHexString(ReplicaProtocolCodec.Serialize(state)));
        await Assert.That(log.TermAt(1)).IsEqualTo(1L);
    }

    private static async Task WithOwnerAsync(ReplicaConfiguration configuration,
        Func<ZoneTreeStore, DurableReplicaLog, Task> operation)
    {
        ZoneTreeStore? store = null;
        DurableReplicaLog? log = null;
        List<Exception> failures = [];
        await ReplicaMaterializerLifecycleErrors.AttemptAsync(async () =>
        {
            store = new(new(configuration.Directory)
            {
                Incarnation = configuration.Incarnation
            },
                RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            log = new(store, RecoveryExecutionOptions.Configuration(configuration));
            await operation(store, log);
        }, failures);
        ReplicaMaterializerLifecycleErrors.Attempt(() => log?.Dispose(), failures);
        ReplicaMaterializerLifecycleErrors.Attempt(() => store?.Dispose(), failures);
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }
}
