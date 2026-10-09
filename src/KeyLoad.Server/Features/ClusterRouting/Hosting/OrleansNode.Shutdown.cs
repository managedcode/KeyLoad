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
        if (Movement is { } moving)
        { await ServerFailureObserver.ObserveAsync(() => moving.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (RemoteDocuments is { } remote)
        { await ServerFailureObserver.ObserveAsync(() => remote.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        await PhysicalOwnerRegistrationRuntime.StopAndJoinAsync(Volatile.Read(ref ownerRegistration),
            physicalOwnerWork, failures).ConfigureAwait(false);
        if (Options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Authority)
        {
            await membershipAuthority.StopAdmissionJoinAndClearProviderAsync(failures).ConfigureAwait(false);
        }
        if (RemoteDocuments is { IsJoined: false } || Movement is { IsJoined: false })
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
