using KeyLoad.Query.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class AnnOwnership
{
    internal static AnnGenerationOwner Create(string directory, StoreIdentity identity,
        IOptions<AnnExecutionOptions> configured, IOptions<PackedAnnOptions> policy,
        IOptions<PackedAnnStorageOptions> storage, int maximumReaders)
    {
        AnnRootLease? lease = null;
        try
        {
            lease = AnnRootLease.Open(directory, identity.NodeId, identity.Incarnation, configured.Value);
            var owner = new AnnGenerationOwner(lease, configured, policy, storage, maximumReaders);
            lease = null;
            return owner;
        }
        catch (Exception primary)
        {
            try
            {
                lease?.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }
}
