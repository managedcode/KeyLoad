using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server.Search;

/// <summary>Identifies the generation's ordered canonical ordinals without persisting a second copy.</summary>
internal static class TextRecordDigest
{
    internal static byte[] Calculate(IReadOnlyList<TextRecord> records, ReadExecutionBudget? budget = null)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var writer = new ArrayBufferWriter<byte>();
        Span<byte> frameLength = stackalloc byte[sizeof(int)];
        foreach (var record in records)
        {
            budget?.Check();
            writer.Clear();
            OrleansSerialization.SerializeToWriter(record, writer);
            BinaryPrimitives.WriteInt32LittleEndian(frameLength, writer.WrittenCount);
            digest.AppendData(frameLength);
            digest.AppendData(writer.WrittenSpan);
        }
        budget?.Check();
        return digest.GetHashAndReset();
    }
}
