using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

internal sealed partial class OrleansNode
{
    private readonly TenantPlacementNodeOwner tenantPlacementOwner = new();
}

internal sealed class TenantPlacementNodeOwner : IAsyncDisposable
{
    private TenantPlacementNativeRuntime? runtime;

    internal TenantPlacementNativeRuntime? Runtime => Volatile.Read(ref runtime);

    internal async Task InitializeAsync(OrleansNode node, PartitionHost partition,
        ServerRuntimeOptions options, PartitionMovementRuntime? movement, TimeProvider clock,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var original = await TenantPlacementNativeFactory.CreateAsync(node, partition,
            options, movement, clock).ConfigureAwait(false);
        Volatile.Write(ref runtime, original);
        token.ThrowIfCancellationRequested();
    }

    internal void Start() => Runtime?.Start();
    internal Task CloseAdmissionAsync() => Runtime?.CloseAdmissionAsync() ?? Task.CompletedTask;
    internal Task JoinAsync() => DisposeAsync().AsTask();
    public ValueTask DisposeAsync() => Volatile.Read(ref runtime)?.DisposeAsync() ?? ValueTask.CompletedTask;
}
