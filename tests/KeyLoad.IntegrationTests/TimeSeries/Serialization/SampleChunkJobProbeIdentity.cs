using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkJobProbeIdentity
{
    internal static Guid OriginalMerge(SampleChunkJobRevocationScenario scenario)
    {
        var partition = scenario.Window.Native.Partition;
        var digest = SHA256.HashData(KeyCodec.Encode(SampleChunkJobRevocationProtocol.IdentityAlias,
            partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey,
            TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, scenario.Window.WindowId,
            SampleChunkRf3Protocol.SealedGeneration, SampleChunkRf3Protocol.CorrectedRevision,
            SampleChunkProtocol.MergeKind, scenario.Creator.PolicyEpoch));
        return new Guid(digest.AsSpan(SampleChunkJobRevocationProtocol.FirstIndex,
            SampleChunkJobRevocationProtocol.GuidBytes));
    }
}
