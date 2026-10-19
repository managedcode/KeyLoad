using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Server;

internal sealed record FileStorageScope(FileAddress Address, ResourceIncarnation Incarnation,
    string PhysicalNamespace, string Directory);
