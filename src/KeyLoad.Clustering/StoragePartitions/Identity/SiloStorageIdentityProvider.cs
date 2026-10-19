using KeyLoad.Core;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace KeyLoad.Clustering;

/// <summary>Reads the actual local Orleans address and persisted storage identity for addressed operations.</summary>
public sealed class SiloStorageIdentityProvider(ILocalSiloDetails silo, IOptions<ClusterOptions> cluster,
    DatabaseEngine database)
{
    /// <summary>Execution Orleans address including the runtime generation.</summary>
    public string RuntimeAddress => silo.SiloAddress.ToParsableString();

    /// <summary>Captures current identity without keeping a second membership registry.</summary>
    public SiloStorageIdentity Read() => new(silo.Name, cluster.Value.ClusterId,
        database.Store.Identity.Incarnation, RuntimeAddress);
}
