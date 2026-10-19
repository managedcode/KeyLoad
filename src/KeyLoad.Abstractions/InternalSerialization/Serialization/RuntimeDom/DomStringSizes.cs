using System.Text.Json;

namespace KeyLoad.Features.InternalSerialization;

internal sealed class DomStringSizes
{
    private const long EmptyEncodedBytes = 0;
    private const int FirstCharacterOffset = 0;
    private const int LastCharacterOffset = 1;
    private const int ChunkCharacters = 4096;
    private readonly Dictionary<string, long> escaped = new(ReferenceEqualityComparer.Instance);

    internal long Escaped(string text)
    {
        if (!escaped.TryGetValue(text, out var bytes))
        {
            bytes = CountEscaped(text);
            escaped.Add(text, bytes);
        }
        return bytes;
    }

    private static long CountEscaped(string text)
    {
        if (text.Length > SerializationLimits.MaximumDomBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
        var bytes = EmptyEncodedBytes;
        var offset = FirstCharacterOffset;
        while (offset < text.Length)
        {
            var count = Math.Min(ChunkCharacters, text.Length - offset);
            if (offset + count < text.Length && char.IsHighSurrogate(text[offset + count - LastCharacterOffset]))
            {
                count--;
            }
            bytes = checked(bytes + JsonEncodedText.Encode(text.AsSpan(offset, count)).EncodedUtf8Bytes.Length);
            if (bytes > SerializationLimits.MaximumDomBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
            }
            offset += count;
        }
        return bytes;
    }
}
