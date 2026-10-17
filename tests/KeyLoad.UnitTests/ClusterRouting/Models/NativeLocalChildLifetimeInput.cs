using KeyLoad.Clustering;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeLocalChildLifetimeInput(PartitionQueryOwnedLeafRequestV2 Request,
    DecodedGrainRequest Verified);
