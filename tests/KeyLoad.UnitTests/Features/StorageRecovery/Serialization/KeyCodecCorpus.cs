using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class KeyCodecCorpus
{
    internal const int ValueCount = 10_000;
    internal const int ScaleCount = 29;
    internal const int DecimalScaleBitShift = 16;
    internal const int DecimalScaleMask = 0x7f;

    private const int Seed = 1701;
    private const int SignPeriod = 2;
    private const int InputLength = sizeof(int) * 2;
    private const int LowWordOffset = 0;
    private const int MiddleWordOffset = sizeof(int);
    private const int HighWordOffset = sizeof(int) * 2;

    internal static decimal[] Create()
    {
        var values = new decimal[ValueCount];
        Span<byte> input = stackalloc byte[InputLength];
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];

        for (var index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(input[LowWordOffset..MiddleWordOffset], Seed);
            BinaryPrimitives.WriteInt32LittleEndian(input[MiddleWordOffset..], index);
            _ = SHA256.HashData(input, digest);

            values[index] = new decimal(
                BinaryPrimitives.ReadInt32LittleEndian(digest[LowWordOffset..MiddleWordOffset]),
                BinaryPrimitives.ReadInt32LittleEndian(digest[MiddleWordOffset..HighWordOffset]),
                BinaryPrimitives.ReadInt32LittleEndian(digest[HighWordOffset..]),
                index % SignPeriod == 0,
                (byte)(index % ScaleCount));
        }

        return values;
    }
}
