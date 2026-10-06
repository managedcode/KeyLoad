using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkUtf16
{
    internal static void Write(string value, Span<byte> destination, ReadExecutionBudget budget)
    {
        const int IndexInitialValue = 0;
        const int CharacterCancellationStrideMask = 0x7FFF;
        const int CancellationStrideStart = 0;

        for (var index = IndexInitialValue; index < value.Length; index++)
        {
            if ((index & CharacterCancellationStrideMask) == CancellationStrideStart)
            {
                budget.Check();
            }
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(index * sizeof(char)), value[index]);
        }
        budget.Check();
    }

    internal static string Decode(ReadOnlySpan<byte> bytes, ReadExecutionBudget budget)
    {
        const int IndexInitialValue = 0;
        const int CharacterCancellationStrideMask = 0x7FFF;
        const int CancellationStrideStart = 0;
        const int StartEmptyCount = 0;

        if (BitConverter.IsLittleEndian)
        {
            return new string(MemoryMarshal.Cast<byte, char>(bytes));
        }
        var characterCount = bytes.Length / sizeof(char);
        var characters = ArrayPool<char>.Shared.Rent(characterCount);
        try
        {
            for (var index = IndexInitialValue; index < characterCount; index++)
            {
                if ((index & CharacterCancellationStrideMask) == CancellationStrideStart)
                {
                    budget.Check();
                }
                characters[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(index * sizeof(char))..]);
            }
            return new string(characters.AsSpan(StartEmptyCount, characterCount));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(characters, clearArray: true);
        }
    }

    internal static void ValidateFallback(ReadOnlySpan<byte> bytes, ReadExecutionBudget budget)
    {
        const int EmptyTextBytes = 0;
        const int OddByteLengthMask = 1;
        const int OffsetInitialValue = 0;
        const int ByteCancellationStrideMask = 0xFFFF;
        const int CancellationStrideStart = 0;

        SampleChunkWire.Require(bytes.Length > EmptyTextBytes && (bytes.Length & OddByteLengthMask) == EmptyTextBytes);
        var pendingHigh = false;
        var hasUnpaired = false;
        for (var offset = OffsetInitialValue; offset < bytes.Length; offset += sizeof(char))
        {
            if ((offset & ByteCancellationStrideMask) == CancellationStrideStart)
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
