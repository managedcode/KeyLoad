using KeyLoad.Clustering;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class SampleChunkMinimumEnvelopeIssuance
{
    internal static GrainRequestEnvelope Create(GrainRequestCodec codec, PrincipalRecord reader, Guid requestId,
        ReadSampleChunkWindowRequest request)
        => codec.CreateRead(requestId, reader.Id, GrainReadKind.SampleChunkWindow, request);

    /// <summary>The scope fault a receiving grain refuses before routing: a foreign incarnation.</summary>
    internal static IEnumerable<Func<GrainRequestEnvelope, GrainRequestEnvelope>> OutOfScope()
    {
        yield return static original => original with
        {
            Incarnation = Guid.NewGuid()
        };
    }

    internal static ReadSampleChunkWindowRequest Decode(GrainRequestCodec codec, GrainRequestEnvelope original,
        Guid requestId, ReadSampleChunkWindowRequest expected)
    {
        var verified = codec.DecodeRead(original, requestId);
        var actual = GrainPayload.Read<ReadSampleChunkWindowRequest>(verified.Payload);
        SampleChunkExplicitMinimumUnitReads.RequireBytes(expected, actual);
        return actual;
    }
}
