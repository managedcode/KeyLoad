using Orleans.Runtime.Services;

namespace KeyLoad.Replication;

/// <summary>Addresses storage services using native Orleans membership addresses.</summary>
public sealed class SiloDatabaseClient(IServiceProvider services) : GrainServiceClient<ISiloDatabaseService>(services)
{
    /// <summary>Borrows the storage service on the exact native silo generation.</summary>
    public ISiloDatabaseService On(SiloAddress address) => GetGrainService(address);
}
