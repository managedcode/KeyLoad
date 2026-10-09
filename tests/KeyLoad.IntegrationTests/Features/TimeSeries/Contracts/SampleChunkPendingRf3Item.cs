using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkPendingRf3Item(SampleChunkJobRevocationScenario Scope, string Series, Guid WindowId)
{
    internal CommandRequest? OriginalSeal { get; set; }
    internal CommitReceipt? SealReceipt { get; set; }
    internal CommandRequest? OriginalCorrection { get; set; }
    internal CommitReceipt? CorrectionReceipt { get; set; }
    internal PartitionRef Partition => Scope.Window.Native.Partition;
    internal ReadSampleChunkWindowRequest Request => new(Partition, TimeSeriesRf3Scenario.Set, Series,
        WindowId, null, null, Limit: SampleChunkRf3Protocol.OutputLimit);
    internal SampleRecord[] Expected =>
    [new(Series, Scope.Window.First, SampleChunkRf3Protocol.FirstSequence, TimeSeriesRf3Scenario.PrivateTags),
     new(Series, Scope.Window.Equal, SampleChunkRf3Protocol.InitialSequence, TimeSeriesRf3Scenario.PrivateTags),
     new(Series, Scope.Window.Late, SampleChunkRf3Protocol.CorrectedSequence, TimeSeriesRf3Scenario.PrivateTags)];

    internal Guid CommandId
    {
        get
        {
            var digest = SHA256.HashData(KeyCodec.Encode(SampleChunkPendingRf3Protocol.IdentityAlias,
                Partition.TenantId, Partition.DatabaseId, Partition.TransactionDomainId, Partition.PartitionKey,
                TimeSeriesRf3Scenario.Set, Series, WindowId, SampleChunkRf3Protocol.SealedGeneration,
                SampleChunkRf3Protocol.CorrectedRevision, SampleChunkProtocol.MergeKind, Scope.Creator.PolicyEpoch));
            return new Guid(digest.AsSpan(SampleChunkPendingRf3Protocol.FirstIndex, SampleChunkPendingRf3Protocol.GuidBytes));
        }
    }
}
