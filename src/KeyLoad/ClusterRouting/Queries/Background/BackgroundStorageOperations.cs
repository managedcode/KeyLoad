using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Reads only the exact files and work owner retained by this open storage scope.</summary>
internal sealed partial class BackgroundStorageOperations(PartitionStorageScope storage,
    IOptions<DueCoordinationOptions> due, TimeProvider clock) : IBackgroundStorageOperations
{
    private DatabaseEngine Database => storage.Host.Database;
}
