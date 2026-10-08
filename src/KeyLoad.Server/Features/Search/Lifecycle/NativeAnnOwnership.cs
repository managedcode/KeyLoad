using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnOwnership
{
    internal static NativeAnnGenerationOwner Create(string directory, StoreIdentity identity,
        IOptions<NativeAnnExecutionOptions> configured, IOptions<PackedAnnOptions> policy,
        IOptions<PackedAnnStorageOptions> storage, int maximumReaders)
    {
        NativeAnnRootLease? lease = null;
        try
        {
            lease = NativeAnnRootLease.Open(directory, identity.NodeId, identity.Incarnation, configured.Value);
            var owner = new NativeAnnGenerationOwner(lease, configured, policy, storage, maximumReaders);
            lease = null;
            return owner;
        }
        catch (Exception primary)
        {
            try
            { lease?.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }
}
