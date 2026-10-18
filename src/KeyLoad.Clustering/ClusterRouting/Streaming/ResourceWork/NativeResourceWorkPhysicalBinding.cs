namespace KeyLoad.Clustering;

/// <summary>Binds request children to the actual silo-local storage identity.</summary>
internal sealed class NativeResourceWorkPhysicalBinding
{
    private PhysicalShardRecord? physical;

    internal void Bind(PhysicalShardRecord actualOwner)
    {
        ArgumentNullException.ThrowIfNull(actualOwner);
        if (physical is not null && physical != actualOwner)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        physical = actualOwner;
    }

    internal void RequireActual(PhysicalShardRecord actualOwner, SiloStorageIdentity runtime)
    {
        if (physical != actualOwner || string.IsNullOrEmpty(runtime.SiloAddress)
            || runtime.Incarnation != actualOwner.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
    }
}
