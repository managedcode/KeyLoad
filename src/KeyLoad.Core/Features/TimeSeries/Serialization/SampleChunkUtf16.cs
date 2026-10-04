using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkUtf16
{
    internal static void Write(string value, Span<byte> destination, ReadExecutionBudget budget)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if ((index & 0x7FFF) == 0)
            {
                budget.Check();
            }
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(index * sizeof(char)), value[index]);
        }
        budget.Check();
    }

    internal static string Decode(ReadOnlySpan<byte> bytes, ReadExecutionBudget budget)
    {
        if (BitConverter.IsLittleEndian)
        {
            return new string(MemoryMarshal.Cast<byte, char>(bytes));
        }
        var characterCount = bytes.Length / sizeof(char);
        var characters = ArrayPool<char>.Shared.Rent(characterCount);
        try
        {
            for (var index = 0; index < characterCount; index++)
            {
                if ((index & 0x7FFF) == 0)
                {
                    budget.Check();
                }
                characters[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(index * sizeof(char))..]);
            }
            return new string(characters.AsSpan(0, characterCount));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(characters, clearArray: true);
        }
    }

    internal static void ValidateFallback(ReadOnlySpan<byte> bytes, ReadExecutionBudget budget)
    {
        SampleChunkWire.Require(bytes.Length > 0 && (bytes.Length & 1) == 0);
        var pendingHigh = false;
        var hasUnpaired = false;
        for (var offset = 0; offset < bytes.Length; offset += sizeof(char))
        {
            if ((offset & 0xFFFF) == 0)
            {
                budget.Check();
            }
            var current = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
            if (pendingHigh)
            {
                if (char.IsLowSurrogate(current))
                {
                    pendingHigh = false;
                    continue;
                }
                hasUnpaired = true;
                pendingHigh = false;
            }
            if (char.IsHighSurrogate(current))
            {
                pendingHigh = true;
            }
            else if (char.IsLowSurrogate(current))
            {
                hasUnpaired = true;
            }
        }
        SampleChunkWire.Require(hasUnpaired || pendingHigh);
        budget.Check();
    }
}
