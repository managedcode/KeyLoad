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

    internal static string Create(GrainRequestCodec codec, PrincipalRecord reader, Guid requestId,
        ReadSampleChunkWindowRequest request)
        => codec.CreateRead(requestId, reader.Id, GrainReadKind.SampleChunkWindow, OrleansSerialization.Serialize(request));

    internal static string ReplaceMinimum(GrainRequestCodec codec, string original, Guid requestId,
        CommitToken? minimum)
    {
        var verified = codec.VerifyRead(original, requestId);
        var before = GrainPayload.Read<ReadSampleChunkWindowRequest>(verified.Payload);
        var replacement = before with
        {
            MinimumToken = minimum
        };
        var payload = OrleansSerialization.Serialize(replacement);
        if (verified.Payload.Span.SequenceEqual(payload))
        {
            throw new InvalidOperationException("The generated execution payload omitted the changed minimum.");
        }
        var envelope = verified.Envelope with
        {
            Payload = payload
        };
        var changed = OrleansSerialization.Serialize(envelope);
        var encoded = Convert.ToBase64String(changed).TrimEnd(Padding)
            .Replace(StandardPlus, UrlMinus).Replace(StandardSlash, UrlUnderscore);
        return original[..(original.IndexOf(Separator, StringComparison.Ordinal) + 1)] + encoded
            + original[original.LastIndexOf(Separator)..];
    }

    internal static ReadSampleChunkWindowRequest Decode(GrainRequestCodec codec, string original,
        Guid requestId, ReadSampleChunkWindowRequest expected)
    {
        var verified = codec.VerifyRead(original, requestId);
        var actual = GrainPayload.Read<ReadSampleChunkWindowRequest>(verified.Payload);
        SampleChunkExplicitMinimumUnitReads.RequireBytes(expected, actual);
        return actual;
    }
}
