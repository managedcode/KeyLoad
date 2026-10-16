using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextMaintenanceBinding
{
    internal static NativeRequestWorkLease Parent(IResourceLifetimeWorkBinding binding)
    {
        if (binding is not INativeRequestWorkBinding native
            || !ReferenceEquals(binding.ResultRetention, native.OriginalProducer.ResultRetention))
        {
            throw NativeTextErrors.Ownership();
        }
        return native.OriginalProducer.RequireOriginalParentBinding();
    }
}
