using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

internal sealed partial class OrleansNode
{
    private async Task StopCoreAsync(Task registered, Task? starting)
    {
        await registered.ConfigureAwait(false);
        if (starting is not null)
        {
            await starting.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        Volatile.Read(ref physicalShardCatalog)?.CloseAdmission();
        var failures = new List<Exception>();
        var stopping = Volatile.Read(ref host);
        // Do not resolve services during cleanup: owners were captured by the original startup path.
        await OrleansNodeNativeWorkShutdown.JoinAsync(tenantPlacementOwner, GraphResources,
            Volatile.Read(ref nativeModelReceiver), Volatile.Read(ref nativeModelClient),
            Volatile.Read(ref nativeBlobWireObserver), Movement, RemoteDocuments, failures).ConfigureAwait(false);
        await PhysicalOwnerRegistrationRuntime.StopAndJoinAsync(Volatile.Read(ref ownerRegistration),
            physicalOwnerWork, failures).ConfigureAwait(false);
        if (RemoteDocuments is { IsJoined: false } || Movement is { IsJoined: false } || !GraphResources.IsJoined)
        {
            ServerFailureObserver.ThrowIfAny(failures);
            throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RequestWorkNotJoined);
        }
        if (stopping is not null)
        {
            await StopHostAsync(stopping, failures).ConfigureAwait(false);
        }
        else
        {
            await ServerFailureObserver.ObserveAsync(requestWork.DrainAsync, failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(() => requestWork.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private Task StopHostAsync(IHost stopping, List<Exception> failures)
        => OrleansNodeHostShutdown.JoinAsync(stopping, partition, requestWork, runtimeOptions, runtimeClock,
            () => Volatile.Write(ref grains, null), () => Volatile.Write(ref host, null), failures);

}

internal static class OrleansNodeNativeWorkShutdown
{
    internal static async Task JoinAsync(TenantPlacementNodeOwner placement,
        global::KeyLoad.Server.Features.GraphTraversal.RemoteGraphNodeResources graphs,
        global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeRuntime? receiver,
        global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeClient? client,
        global::KeyLoad.Server.Features.BlobStorage.NativeBlobWireProbeObserver? blobObserver,
        global::KeyLoad.Server.Features.ClusterRouting.PartitionMovementRuntime? movement,
        global::KeyLoad.Server.Features.DocumentStorage.RemoteDocumentRuntime? documents, List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(placement.CloseAdmissionAsync, failures).ConfigureAwait(false);
        await graphs.JoinAsync(failures).ConfigureAwait(false);
        if (receiver is not null)
        {
            await ServerFailureObserver.ObserveAsync(receiver.StopAsync, failures).ConfigureAwait(false);
        }
        if (client is not null)
        {
            await ServerFailureObserver.ObserveAsync(client.StopAsync, failures).ConfigureAwait(false);
        }
        if (blobObserver is not null)
        {
            await ServerFailureObserver.ObserveAsync(blobObserver.StopAsync, failures).ConfigureAwait(false);
        }
        if (movement is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => movement.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(placement.JoinAsync, failures).ConfigureAwait(false);
        if (documents is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => documents.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
    }
}
