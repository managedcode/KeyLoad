using KeyLoad.Clustering.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class CacheControlWireMutationAssertions
{
    private const string InvalidTranscriptMessage = "The valid mutation source did not encode.";
    private const string PartialOutputMessage = "Invalid input must leave the output byte array empty.";

    internal static async Task AssertCanonicalChangedAsync(byte[] original, ICacheControlMessage changed)
    {
        var mutated = Canonical(changed);
        await Assert.That(mutated.AsSpan().SequenceEqual(original)).IsFalse();
    }

    internal static async Task AssertSigningChangedAsync(byte[] original, ICacheControlMessage changed)
    {
        var mutated = Signing(changed);
        await Assert.That(mutated.AsSpan().SequenceEqual(original)).IsFalse();
    }

    internal static async Task AssertSigningUnchangedAsync(byte[] original, ICacheControlMessage changed)
    {
        var mutated = Signing(changed);
        await Assert.That(mutated.AsSpan().SequenceEqual(original)).IsTrue();
    }

    internal static byte[] Canonical(ICacheControlMessage message)
    {
        var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
        return encoded ? bytes : throw new InvalidOperationException(InvalidTranscriptMessage);
    }

    internal static byte[] Signing(ICacheControlMessage message)
    {
        var encoded = CacheControlWire.TryEncodeForSigning(message, out var bytes);
        return encoded ? bytes : throw new InvalidOperationException(InvalidTranscriptMessage);
    }

    internal static bool Encode(ICacheControlMessage message)
    {
        var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
        if (!encoded && bytes.Length != 0)
        {
            throw new InvalidOperationException(PartialOutputMessage);
        }

        return encoded;
    }
}
