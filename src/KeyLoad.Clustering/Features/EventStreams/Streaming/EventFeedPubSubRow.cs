
namespace KeyLoad.Clustering;

internal sealed record EventFeedPubSubRow(GrainId Key, Type StateType, byte[] Value,
    string ETag, long EncodedBytes);
