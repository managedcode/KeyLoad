using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaPrefixGcOwners
{
    internal static async Task<T> NodeAsync<T>(string root, Guid incarnation, Func<ReplicaCrashNode, Task<T>> operation,
        ReadOnlyMemory<byte>? canonicalSigningKey = null)
    {
        var failures = new List<Exception>();
        ReplicaCrashNode? node = null;
        T result = default!;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            node = ReplicaCrashNode.OpenTarget(root, incarnation, canonicalSigningKey: canonicalSigningKey);
            result = await operation(node).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (node is { } original)
        { ServerFailureObserver.Observe(original.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    internal static Task NodeAsync(string root, Guid incarnation, Func<ReplicaCrashNode, Task> operation,
        ReadOnlyMemory<byte>? canonicalSigningKey = null)
        => NodeAsync(root, incarnation, async node =>
        { await operation(node).ConfigureAwait(false); return true; }, canonicalSigningKey);

    internal static async Task MaterializerAsync(ReplicaCrashNode node, Func<ReplicaMaterializer, Task> operation)
    {
        var failures = new List<Exception>();
        ReplicaMaterializer? materializer = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            materializer = new(node.Database, node.Log, node.Snapshots, RecoveryExecutionOptions.Replica());
            await operation(materializer).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (materializer is { } original)
        { await ServerFailureObserver.ObserveAsync(() => original.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
