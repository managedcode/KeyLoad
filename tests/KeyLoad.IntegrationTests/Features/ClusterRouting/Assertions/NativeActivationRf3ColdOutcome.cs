using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationRf3ColdOutcome
{
    internal static async Task VerifyAsync(TwoRf3MembershipWave wave, RequestCqrsPhaseFaultIdentity identity,
        CommandRequest command, CommitReceipt receipt, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.KillAsync(node,
            NativeActivationRf3Protocol.Cold, cancellationToken), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var root = Path.Combine(wave.OwnedDataRoot, node);
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "replica", "owner.lock"));
        }
        var outcomes = new Dictionary<string, OperationResult?>(StringComparer.Ordinal);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        { outcomes.Add(node, Read(wave, node, identity, command)); }
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        { await Assert.That(outcomes[node]).IsNotNull(); }
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Except(RequestCqrsProbeFixtureProtocol.Nodes, StringComparer.Ordinal))
        { await Assert.That(outcomes[node]).IsNull(); }
        foreach (var outcome in outcomes.Values.Where(value => value is not null))
        {
            await Assert.That(outcome!.Error).IsNull();
            await Assert.That(outcome.SafeDetail).IsNull();
            await Assert.That(JsonDefaults.Serialize(outcome.Get<CommitReceipt>()).SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        }
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(cancellationToken).ConfigureAwait(false);
    }

    private static OperationResult? Read(TwoRf3MembershipWave wave, string node,
        RequestCqrsPhaseFaultIdentity identity, CommandRequest command)
    {
        ZoneTreeStore? store = null;
        OperationResult? result = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            store = new(new(Path.Combine(wave.OwnedDataRoot, node, "database")),
                IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
            result = store.Read(view => CommandOutcomeKeyResolver.Select(view, identity.PrincipalId,
                command.CommandId, new(CommandOutcomeScopeKind.Partition, identity.Partition)).Outcome?.Result);
        }, failures);
        if (store is { } owned)
        { ServerFailureObserver.Observe(owned.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }
}
