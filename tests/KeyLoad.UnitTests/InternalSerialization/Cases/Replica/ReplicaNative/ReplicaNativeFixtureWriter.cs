
namespace KeyLoad.UnitTests;

/// <summary>Malformed fixtures are authored by native codecs inside the authoritative shared envelope.</summary>
internal static class ReplicaNativeFixtureWriter
{
    internal static byte[] Vote(ReplicaMalformedVoteShape shape, VoteRequest value)
        => Encode(value, new ReplicaMalformedVoteCodec(shape));

    internal static byte[] Batch(ReplicaMalformedArrayShape shape, ReplicaEntryBatch value)
        => Encode(value, new ReplicaMalformedBatchCodec(shape));

    internal static byte[] State(ReplicaMalformedVoteShape shape, ReplicaHardState value)
        => Encode(value, new ReplicaMalformedHardStateCodec(shape));

    internal static byte[] Encode<T, TCodec>(T value, TCodec codec) where TCodec : class
        => ReplicaNativeFixtureEncoder.Encode(value, codec);
}
