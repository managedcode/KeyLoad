using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

internal sealed partial class OrleansNode
{
    private async Task StopCoreAsync(Task registered, Task? starting)
    {
        await registered.ConfigureAwait(false);
        if (starting is not null)
        { await starting.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }
        Volatile.Read(ref physicalShardCatalog)?.CloseAdmission();
        var failures = new List<Exception>();
        var stopping = Volatile.Read(ref host);
        // Do not resolve services during cleanup: owners were captured by the original startup path.
        await GraphResources.JoinAsync(failures).ConfigureAwait(false);
        if (Volatile.Read(ref nativeModelReceiver) is { } nativeReceiver)
        { await ServerFailureObserver.ObserveAsync(nativeReceiver.StopAsync, failures).ConfigureAwait(false); }
        if (Volatile.Read(ref nativeModelClient) is { } nativeClient)
        { await ServerFailureObserver.ObserveAsync(nativeClient.StopAsync, failures).ConfigureAwait(false); }
        if (Volatile.Read(ref nativeBlobWireObserver) is { } blobObserver)
        { await ServerFailureObserver.ObserveAsync(blobObserver.StopAsync, failures).ConfigureAwait(false); }
        if (Movement is { } moving)
        { await ServerFailureObserver.ObserveAsync(() => moving.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (RemoteDocuments is { } remote)
        { await ServerFailureObserver.ObserveAsync(() => remote.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
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
