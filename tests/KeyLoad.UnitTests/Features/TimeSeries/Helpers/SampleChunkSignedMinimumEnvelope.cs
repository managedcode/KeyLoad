using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkSignedMinimumEnvelope
{
    private const char Separator = '.';
    private const char Padding = '=';
    private const char StandardPlus = '+';
    private const char StandardSlash = '/';
    private const char UrlMinus = '-';
    private const char UrlUnderscore = '_';
    private const int NextCharacter = 1;

    internal static string Create(GrainRequestCodec codec, PrincipalRecord reader, Guid requestId,
        ReadSampleChunkWindowRequest request)
        => codec.CreateRead(requestId, reader.Id, GrainReadKind.SampleChunkWindow, NativeSerialization.Serialize(request));

    internal static string ReplaceMinimum(GrainRequestCodec codec, string original, Guid requestId,
        CommitToken? minimum)
    {
        var verified = codec.VerifyRead(original, requestId);
        var before = GrainNativePayload.Read<ReadSampleChunkWindowRequest>(verified.Payload);
        var replacement = before with
        {
            MinimumToken = minimum
        };
        var payload = NativeSerialization.Serialize(replacement);
        if (verified.Payload.Span.SequenceEqual(payload))
        {
            throw new InvalidOperationException("The generated native payload omitted the changed minimum.");
        }
        var envelope = verified.Envelope with
        {
            Payload = payload
        };
        var changed = NativeSerialization.Serialize(envelope);
        var encoded = Convert.ToBase64String(changed).TrimEnd(Padding)
            .Replace(StandardPlus, UrlMinus).Replace(StandardSlash, UrlUnderscore);
        return original[..(original.IndexOf(Separator, StringComparison.Ordinal) + NextCharacter)] + encoded
            + original[original.LastIndexOf(Separator)..];
    }

    internal static ReadSampleChunkWindowRequest Decode(GrainRequestCodec codec, string original,
        Guid requestId, ReadSampleChunkWindowRequest expected)
    {
        var verified = codec.VerifyRead(original, requestId);
        var actual = GrainNativePayload.Read<ReadSampleChunkWindowRequest>(verified.Payload);
        SampleChunkExplicitMinimumUnitReads.RequireBytes(expected, actual);
        return actual;
    }
}
